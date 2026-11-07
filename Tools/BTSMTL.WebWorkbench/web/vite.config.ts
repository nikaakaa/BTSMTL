import { defineConfig, loadEnv, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

const host = 'http://127.0.0.1:5367';
const extensionModule = 'virtual:btsmtl-workbench-extensions';

function hostExtensions(entries: readonly string[]): Plugin {
  const resolved = '\0' + extensionModule;
  return {
    name: 'btsmtl-workbench-extensions',
    resolveId: id => id === extensionModule ? resolved : null,
    load: id => id !== resolved ? null : [
      ...entries.map((entry, index) => `import extension${index} from ${JSON.stringify(entry)};`),
      `export default [${entries.map((_, index) => `extension${index}`).join(', ')}];`,
    ].join('\n'),
  };
}

// 宿主扩展入口由宿主构建经 BTSMTL_WORKBENCH_EXTENSIONS 给出（分号分隔的绝对路径）；未给出时工作台不含任何宿主面板。
// 产物默认写入本工作台 wwwroot，宿主组合扩展时经 BTSMTL_WORKBENCH_OUT 写入其产品目录；构建前清空，产物不入库。
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'BTSMTL_WORKBENCH_');
  const entries = (env.BTSMTL_WORKBENCH_EXTENSIONS ?? '').split(';').map(item => item.trim()).filter(Boolean);
  return {
    plugins: [react(), hostExtensions(entries)],
    resolve: {
      alias: { '@btsmtl/workbench': '/src/sdk.ts' },
      // 扩展源码位于宿主仓库，第三方依赖统一从本工作台解析，与工作台共用同一份 React 与状态库。
      dedupe: ['react', 'react-dom', 'zustand'],
    },
    build: {
      outDir: env.BTSMTL_WORKBENCH_OUT || '../wwwroot',
      emptyOutDir: true,
    },
    server: {
      host: '127.0.0.1',
      port: 5368,
      strictPort: true,
      fs: { allow: ['.', ...entries.map(entry => entry.replace(/[\\/][^\\/]+$/, ''))] },
      proxy: {
        '/api': { target: host, ws: true, changeOrigin: false },
      },
    },
  };
});
