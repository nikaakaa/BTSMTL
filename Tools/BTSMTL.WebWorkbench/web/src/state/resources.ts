import { getBlob } from '../api/http';
import { resourceApi } from '../api/resources';
import type { AnimationLibrary } from '../api/types';

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
