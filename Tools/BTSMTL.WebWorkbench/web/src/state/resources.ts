import { useEffect, useMemo, useState } from 'react';
import { getBlob } from '../api/http';
import { resourceApi } from '../api/resources';
import type { AnimationLibrary } from '../api/types';
import { consoleActions } from './store';

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

// 动画资源清单；未读到时为 null。
export function useAnimationLibrary(enabled: boolean): AnimationLibrary | null {
  const [value, setValue] = useState<AnimationLibrary | null>(null);
  useEffect(() => {
    if (!enabled) return;
    let alive = true;
    resourceCache.library().then(next => { if (alive) setValue(next); }, (error: unknown) => consoleActions.error('动画资源', error));
    return () => { alive = false; };
  }, [enabled]);
  return enabled ? value : null;
}

// 动画资源身份（guid:localFileId）到素材帧率；资源清单未读到时为 null。
export function useAnimationFrameRates(enabled: boolean): ReadonlyMap<string, number> | null {
  const value = useAnimationLibrary(enabled);
  return useMemo(() => value && new Map(value.clips.map(clip => [clip.resource.identity, clip.frameRate])), [value]);
}
