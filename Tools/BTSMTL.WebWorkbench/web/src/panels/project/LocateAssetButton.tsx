import { useState } from 'react';
import { authoringApi, type AssetLocateResult } from '../../api/authoring';
import { consoleActions } from '../../state/store';
import { Button } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';

export type AssetLocateTarget = { kind: 'asset'; identity: string } | { kind: 'unavailable'; reason: string };

type LocateState = { kind: 'idle' } | { kind: 'pending' } | { kind: 'located'; result: AssetLocateResult }
  | { kind: 'failed'; text: string; entryId: number | null };

export function LocateAssetButton({ target }: { target: AssetLocateTarget }) {
  const [state, setState] = useState<LocateState>({ kind: 'idle' });
  const reason = target.kind === 'unavailable' ? target.reason : null;
  return <>
    <Button variant="ghost" disabled={reason !== null || state.kind === 'pending'}
      tip={reason ?? (state.kind === 'located' ? `${state.result.projectPath}\n${state.result.assetPath}` : '在资源管理器中选中该资产的原文件')}
      onClick={target.kind === 'asset' ? () => {
        setState({ kind: 'pending' });
        void authoringApi.locateAsset(target.identity).then(result => setState({ kind: 'located', result }), (error: unknown) => {
          setState({ kind: 'failed', text: error instanceof Error ? error.message : String(error),
            entryId: consoleActions.error('Project', error) });
        });
      } : undefined}>{state.kind === 'pending' ? '正在定位' : state.kind === 'located' ? '已显示' : '在资源管理器中显示'}</Button>
    {state.kind === 'failed' && <ErrorLine {...state} />}
  </>;
}
