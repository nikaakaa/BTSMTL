import { useEffect, useMemo, useState } from 'react';
import { getBlob } from '../api/http';
import { resourceApi } from '../api/resources';
import type { AnimationClip, AnimationLibrary } from '../api/types';
import { readReferenceDrop, type ReferenceCatalog } from './referenceSelection';

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
type ReferencedAnimationClip = AnimationClip & { value: string };
const hasAnimationValue = (clip: AnimationClip): clip is ReferencedAnimationClip => clip.value !== null;

export function readAnimationDrop(transfer: DataTransfer, state: AnimationLibraryState, catalog: ReferenceCatalog):
  { kind: 'selected'; clip: ReferencedAnimationClip } | { kind: 'rejected'; reason: string } {
  const selection = readReferenceDrop(transfer, catalog);
  if (selection.kind === 'rejected') return selection;
  if (state.kind === 'loading') return { kind: 'rejected', reason: '动画库清单尚未取得，原值已保留' };
  if (state.kind === 'unavailable') return { kind: 'rejected', reason: state.reason };
  const clips = state.library.clips.filter((item): item is ReferencedAnimationClip => hasAnimationValue(item) && item.value === selection.choice.value);
  return clips.length === 1 ? { kind: 'selected', clip: clips[0] }
    : { kind: 'rejected', reason: `当前素材库对该资源提供 ${clips.length} 个片段，无法确定素材时长；原值已保留。` };
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

// 动画资源作者值到素材帧率；资源清单未读到时为 null。
export function useAnimationFrameRates(enabled: boolean): ReadonlyMap<string, number> | null {
  const value = useAnimationLibrary(enabled);
  return useMemo(() => value.kind === 'ready'
    ? new Map(value.library.clips.filter(hasAnimationValue).map(clip => [clip.value, clip.frameRate])) : null, [value]);
}
