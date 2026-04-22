import type { Awaitable } from '../metadata/types';
import type { EditorChangeReason } from './types';

export type EntityEditorCommitContext = {
  reloadSucceeded: boolean;
};

export type EntityEditorCreatedContext = EntityEditorCommitContext & {
  id: string;
};

export type EntityEditorChangedContext = EntityEditorCommitContext & {
  reason?: EditorChangeReason | (string & {});
};

type EntityEditorCommitDecision<TContext> =
  | boolean
  | ((context: TContext) => boolean);

export type UseEntityEditorCommitHandlersArgs = {
  reload: () => Awaitable<boolean | void>;
  closeDrawer?: () => Awaitable<void>;
  onCreated?: (context: EntityEditorCreatedContext) => Awaitable<void>;
  onSaved?: (context: EntityEditorCommitContext) => Awaitable<void>;
  onChanged?: (context: EntityEditorChangedContext) => Awaitable<void>;
  onDeleted?: (context: EntityEditorCommitContext) => Awaitable<void>;
  closeOnCreated?: EntityEditorCommitDecision<EntityEditorCreatedContext>;
  closeOnSaved?: EntityEditorCommitDecision<EntityEditorCommitContext>;
  closeOnChanged?: EntityEditorCommitDecision<EntityEditorChangedContext>;
  closeOnDeleted?: EntityEditorCommitDecision<EntityEditorCommitContext>;
};

function shouldClose<TContext>(
  decision: EntityEditorCommitDecision<TContext> | undefined,
  context: TContext,
): boolean {
  if (typeof decision === 'function') return decision(context);
  return decision ?? true;
}

async function reloadWithStatus(
  reload: UseEntityEditorCommitHandlersArgs['reload'],
): Promise<boolean> {
  return await reload() !== false;
}

export function useEntityEditorCommitHandlers(
  args: UseEntityEditorCommitHandlersArgs,
) {
  async function closeIfRequested<TContext>(
    decision: EntityEditorCommitDecision<TContext> | undefined,
    context: TContext,
  ) {
    if (!args.closeDrawer || !shouldClose(decision, context)) return;
    await args.closeDrawer();
  }

  async function handleCreated(id: string) {
    const context: EntityEditorCreatedContext = {
      id,
      reloadSucceeded: await reloadWithStatus(args.reload),
    };

    await args.onCreated?.(context);
    await closeIfRequested(args.closeOnCreated, context);
  }

  async function handleSaved() {
    const context: EntityEditorCommitContext = {
      reloadSucceeded: await reloadWithStatus(args.reload),
    };

    await args.onSaved?.(context);
    await closeIfRequested(args.closeOnSaved, context);
  }

  async function handleChanged(reason?: EditorChangeReason | (string & {})) {
    const context: EntityEditorChangedContext = {
      reason,
      reloadSucceeded: await reloadWithStatus(args.reload),
    };

    await args.onChanged?.(context);
    await closeIfRequested(args.closeOnChanged, context);
  }

  async function handleDeleted() {
    const context: EntityEditorCommitContext = {
      reloadSucceeded: await reloadWithStatus(args.reload),
    };

    await args.onDeleted?.(context);
    await closeIfRequested(args.closeOnDeleted, context);
  }

  return {
    handleCreated,
    handleSaved,
    handleChanged,
    handleDeleted,
  };
}
