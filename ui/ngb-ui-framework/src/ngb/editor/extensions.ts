import type { Component, ComputedRef } from 'vue';

import type { Awaitable } from '../metadata/types';
import type { EditorKind, EditorMode, EntityHeaderIconAction } from './types';

export type EntityEditorRenderExtension = {
  key: string;
  component: Component;
  props?: Record<string, unknown>;
  componentRef?: ((value: unknown) => void) | null;
};

export type EntityEditorActionHandler = () => Awaitable<void>;
export type EntityEditorActionHandlerMap = Record<string, EntityEditorActionHandler | undefined>;

export type UseEntityEditorPageActionsArgs = {
  kind: ComputedRef<EditorKind>;
  mode: ComputedRef<EditorMode>;
  compactTo: ComputedRef<string | null>;
  loading: ComputedRef<boolean>;
  saving: ComputedRef<boolean>;
  isNew: ComputedRef<boolean>;
  isMarkedForDeletion: ComputedRef<boolean>;
  canSave: ComputedRef<boolean>;
  canShareLink: ComputedRef<boolean>;
  canOpenAudit: ComputedRef<boolean>;
  canMarkForDeletion: ComputedRef<boolean>;
  canUnmarkForDeletion: ComputedRef<boolean>;
  extraActions?: ComputedRef<EntityHeaderIconAction[]>;
};

export function runEntityEditorAction(
  action: string,
  handlers: EntityEditorActionHandlerMap,
): boolean {
  const handler = handlers[action];
  if (!handler) return false;
  void Promise.resolve(handler());
  return true;
}
