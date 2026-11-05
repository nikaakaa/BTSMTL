import { useEffect, useMemo, useState } from 'react';
import { getBlob } from '../api/http';
import { resourceApi } from '../api/resources';
import type { AnimationClip, AnimationLibrary } from '../api/types';
import type { ReferenceChoice } from '../api/authoring';
import { readReferenceDrag, type ReferenceCatalog } from './referenceSelection';

// 动画资源数据缓存（D12）：资源清单与同版 glb 只下载一次；面板卸载后再次打开直接取用。

let library: Promise<AnimationLibrary> | null = null;
const files = new Map<string, Promise<ArrayBuffer>>();

export const resourceCache = {
  library(): Promise<AnimationLibrary> {
    library ??= resourceApi.animations().catch(error => { library = null; throw error; });
    return library;
  },
  file(sha256: string): Promise<ArrayBuffer> {
    let file = files.get(sha256);
    if (!file) {
      file = getBlob(resourceApi.animationFilePath(sha256)).then(response => response.arrayBuffer());
      file.catch(() => files.delete(sha256));
      files.set(sha256, file);
    }
    return file;
  },
};

export type AnimationLibraryState =
  | { kind: 'loading' }
  | { kind: 'ready'; library: AnimationLibrary }
  | { kind: 'unavailable'; reason: string };

const loadingLibrary = { kind: 'loading' } satisfies AnimationLibraryState;
export const animationReferenceKind = 'animation-clip';

export const animationChoice = (clip: AnimationClip): ReferenceChoice => ({
  value: clip.key!, name: clip.name,
  detail: `${clip.category} · ${clip.duration.toFixed(3)} 秒 · ${clip.frameRate} fps`,
  source: clip.unityAsset, conflict: null, targets: [], businessKeys: [], associated: [],
});

export function animationReferenceCatalog(state: AnimationLibraryState): ReferenceCatalog {
  switch (state.kind) {
    case 'loading': return { kind: 'unavailable', reason: '动画库清单尚未取得' };
    case 'unavailable': return state;
    case 'ready': return {
      kind: 'ready', referenceKind: animationReferenceKind, items: state.library.clips.filter(clip => clip.key != null).map(animationChoice),
      businessKeysAvailability: null,
      invalidReason: '动画引用已失效，当前动画库缺少该动画，请重新导出动画库；原身份已保留',
    };
  }
}

export function readAnimationDrop(transfer: DataTransfer, state: AnimationLibraryState):
  { kind: 'selected'; clip: AnimationClip } | { kind: 'rejected'; reason: string } {
  if (state.kind === 'loading') return { kind: 'rejected', reason: '动画库清单尚未取得，原值已保留' };
  if (state.kind === 'unavailable') return { kind: 'rejected', reason: state.reason };
  const drag = readReferenceDrag(transfer);
  if (drag.kind === 'rejected') return drag;
  if (drag.referenceKind !== animationReferenceKind)
    return { kind: 'rejected', reason: '动画字段只接受动画库片段，原值已保留' };
  const clip = state.library.clips.find(item => item.key != null && item.key === drag.value);
  return clip ? { kind: 'selected', clip }
    : { kind: 'rejected', reason: '当前动画库缺少该动画，请重新导出动画库；原值已保留' };
}

export function useAnimationLibrary(enabled: boolean): AnimationLibraryState {
  const [value, setValue] = useState<AnimationLibraryState>(loadingLibrary);
  useEffect(() => {
    if (!enabled) return;
    let alive = true;
    resourceCache.library().then(library => { if (alive) setValue({ kind: 'ready', library }); },
      (error: unknown) => { if (alive) setValue({ kind: 'unavailable', reason: error instanceof Error ? error.message : String(error) }); });
    return () => { alive = false; };
  }, [enabled]);
  return enabled ? value : loadingLibrary;
}

// 动画片段键到素材帧率；资源清单未读到时为 null。
export function useAnimationFrameRates(enabled: boolean): ReadonlyMap<string, number> | null {
  const value = useAnimationLibrary(enabled);
  return useMemo(() => value.kind === 'ready'
    ? new Map(value.library.clips.filter(clip => clip.key != null).map(clip => [clip.key!, clip.frameRate])) : null, [value]);
}
