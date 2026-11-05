import { getJson, query } from './http';

export interface ProjectFolderEntry { name: string; path: string }
export interface ProjectFileEntry { name: string; path: string; extension: string; length: number; guid: string | null }
export interface ProjectFolderListing { path: string; folders: ProjectFolderEntry[]; files: ProjectFileEntry[] }

// Project 窗口按磁盘目录读取；空路径返回根目录。
export const projectApi = {
  folder: (path: string, signal?: AbortSignal) =>
    getJson<ProjectFolderListing>(`/api/project/folder${query({ path })}`, signal),
};
