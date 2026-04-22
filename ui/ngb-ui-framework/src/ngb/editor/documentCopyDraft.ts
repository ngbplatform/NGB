import {
  canUseStorage,
  listStorageKeys,
  readStorageString,
  removeStorageItem,
  writeStorageString,
} from '../utils/storage';
import type { JsonObject, JsonValue, RecordFields, RecordParts } from '../metadata/types';

const STORAGE_KEY_PREFIX = 'ngb:document-copy-draft:';
const SNAPSHOT_VERSION = 1;
const MAX_SNAPSHOT_AGE_MS = 6 * 60 * 60 * 1000;

type StoredDocumentCopyDraft = {
  version: number;
  documentType: string;
  fields: RecordFields;
  parts?: RecordParts | null;
  createdAtUtc: string;
};

export type DocumentCopyDraftSnapshot = {
  documentType: string;
  fields: RecordFields;
  parts?: RecordParts | null;
};

type CopyDraftStore = {
  kind: 'session' | 'local' | 'memory';
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
  keys(): string[];
};

type GlobalWithCopyDraftStore = typeof globalThis & {
  __ngbDocumentCopyDraftMemoryStore?: Map<string, string>;
};

function getMemoryStore(): Map<string, string> {
  const root = globalThis as GlobalWithCopyDraftStore;
  if (!root.__ngbDocumentCopyDraftMemoryStore) {
    root.__ngbDocumentCopyDraftMemoryStore = new Map<string, string>();
  }
  return root.__ngbDocumentCopyDraftMemoryStore;
}

function getStores(): CopyDraftStore[] {
  const stores: CopyDraftStore[] = [];
  if (canUseStorage('session')) {
    stores.push({
      kind: 'session',
      getItem: (key) => readStorageString('session', key),
      setItem: (key, value) => {
        void writeStorageString('session', key, value);
      },
      removeItem: (key) => removeStorageItem('session', key),
      keys: () => listStorageKeys('session'),
    });
  }

  if (canUseStorage('local')) {
    stores.push({
      kind: 'local',
      getItem: (key) => readStorageString('local', key),
      setItem: (key, value) => {
        void writeStorageString('local', key, value);
      },
      removeItem: (key) => removeStorageItem('local', key),
      keys: () => listStorageKeys('local'),
    });
  }

  const memory = getMemoryStore();
  stores.push({
    kind: 'memory',
    getItem: (key) => memory.get(key) ?? null,
    setItem: (key, value) => {
      memory.set(key, value);
    },
    removeItem: (key) => {
      memory.delete(key);
    },
    keys: () => Array.from(memory.keys()),
  });

  return stores;
}

function buildStorageKey(token: string): string {
  return `${STORAGE_KEY_PREFIX}${token}`;
}

function createToken(): string {
  const random = Math.random().toString(36).slice(2, 10);
  return `${Date.now().toString(36)}-${random}`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

function cleanupExpiredSnapshots(store: CopyDraftStore): void {
  const now = Date.now();

  for (const key of store.keys()) {
    if (!key || !key.startsWith(STORAGE_KEY_PREFIX)) continue;

    const raw = store.getItem(key);
    if (!raw) {
      store.removeItem(key);
      continue;
    }

    try {
      const parsed = JSON.parse(raw) as Partial<StoredDocumentCopyDraft>;
      const createdAtMs = Date.parse(String(parsed.createdAtUtc ?? ''));
      if (!Number.isFinite(createdAtMs) || now - createdAtMs > MAX_SNAPSHOT_AGE_MS) {
        store.removeItem(key);
      }
    } catch {
      store.removeItem(key);
    }
  }
}

function parseSnapshot(raw: string | null): StoredDocumentCopyDraft | null {
  if (!raw) return null;

  try {
    const parsed = JSON.parse(raw) as Partial<StoredDocumentCopyDraft>;
    if (parsed.version !== SNAPSHOT_VERSION) return null;
    if (typeof parsed.documentType !== 'string' || !parsed.documentType.trim()) return null;
    if (!isRecord(parsed.fields)) return null;
    if (parsed.parts != null && !isRecord(parsed.parts)) return null;
    if (typeof parsed.createdAtUtc !== 'string' || !parsed.createdAtUtc.trim()) return null;
    return {
      version: SNAPSHOT_VERSION,
      documentType: parsed.documentType,
      fields: parsed.fields as RecordFields,
      parts: (parsed.parts ?? null) as RecordParts | null,
      createdAtUtc: parsed.createdAtUtc,
    };
  } catch {
    return null;
  }
}

function sanitizeForStorage(value: unknown, seen: WeakSet<object> = new WeakSet()): JsonValue {
  if (value == null) return null;

  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return value;
  }

  if (typeof value === 'bigint') return value.toString();
  if (typeof value === 'function' || typeof value === 'symbol') return null;

  if (Array.isArray(value)) {
    return value.map((item) => sanitizeForStorage(item, seen));
  }

  if (typeof value === 'object') {
    if (seen.has(value)) return null;
    seen.add(value);

    const out: JsonObject = {};
    for (const [key, item] of Object.entries(value)) {
      if (item === undefined) continue;
      out[key] = sanitizeForStorage(item, seen);
    }
    return out;
  }

  return null;
}

function buildPayload(snapshot: DocumentCopyDraftSnapshot): StoredDocumentCopyDraft {
  return {
    version: SNAPSHOT_VERSION,
    documentType: snapshot.documentType,
    fields: (sanitizeForStorage(snapshot.fields ?? {}) ?? {}) as RecordFields,
    parts: sanitizeForStorage(snapshot.parts ?? null) as RecordParts | null,
    createdAtUtc: new Date().toISOString(),
  };
}

function writeToStore(store: CopyDraftStore, key: string, value: string): boolean {
  try {
    store.setItem(key, value);
    return true;
  } catch {
    return false;
  }
}

export function saveDocumentCopyDraft(snapshot: DocumentCopyDraftSnapshot): string | null {
  const token = createToken();
  const stores = getStores();
  const payload = buildPayload(snapshot);
  const serialized = JSON.stringify(payload);
  const key = buildStorageKey(token);

  for (const store of stores) {
    cleanupExpiredSnapshots(store);
    if (writeToStore(store, key, serialized)) return token;
  }

  return null;
}

export function readDocumentCopyDraft(
  token: string | null | undefined,
  expectedDocumentType?: string | null,
): DocumentCopyDraftSnapshot | null {
  const normalizedToken = String(token ?? '').trim();
  if (!normalizedToken) return null;

  const key = buildStorageKey(normalizedToken);
  for (const store of getStores()) {
    cleanupExpiredSnapshots(store);

    const parsed = parseSnapshot(store.getItem(key));
    if (!parsed) continue;
    if (expectedDocumentType && parsed.documentType !== expectedDocumentType) return null;

    return {
      documentType: parsed.documentType,
      fields: parsed.fields,
      parts: parsed.parts ?? null,
    };
  }

  return null;
}

export function clearDocumentCopyDraft(token: string | null | undefined): void {
  const normalizedToken = String(token ?? '').trim();
  if (!normalizedToken) return;

  const key = buildStorageKey(normalizedToken);
  for (const store of getStores()) {
    store.removeItem(key);
  }
}
