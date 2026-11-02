import { useState } from 'react';
import { authoringApi, type AssetLocateResult, type QueryAvailability } from '../../api/authoring';
import { consoleActions } from '../../state/store';
import { Button } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';

export type AssetLocateTarget = { kind: 'asset'; identity: string } | { kind: 'unavailable'; reason: string };

type LocateState = { kind: 'idle' } | { kind: 'pending' } | { kind: 'located'; result: AssetLocateResult }
  | { kind: 'failed'; text: string; entryId: number | null };

export function LocateAssetButton({ target, availability }: { target: AssetLocateTarget; availability: QueryAvailability }) {
  const [state, setState] = useState<LocateState>({ kind: 'idle' });
  const reason = target.kind === 'unavailable' ? target.reason : availability.available ? null : availability.reason;
  return <>
    <Button variant="ghost" disabled={reason !== null || state.kind === 'pending'}
      tip={reason ?? (state.kind === 'located' ? `${state.result.projectPath}\n${state.result.assetPath}` : '在目标 Unity 工程中选中并定位该资产')}
      onClick={target.kind === 'asset' ? () => {
        setState({ kind: 'pending' });
        void authoringApi.locateAsset(target.identity).then(result => setState({ kind: 'located', result }), (error: unknown) => {
          setState({ kind: 'failed', text: error instanceof Error ? error.message : String(error),
            entryId: consoleActions.error('Project', error) });
        });
      } : undefined}>{state.kind === 'pending' ? '正在定位' : state.kind === 'located' ? '已定位' : '在 Unity 中定位'}</Button>
    {state.kind === 'failed' && <ErrorLine {...state} />}
  </>;
}
