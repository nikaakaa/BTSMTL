import { useEffect, useState } from 'react';
import { authoringApi, type AuthoringProduct } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { authoringActions, authoringDocument, useAuthoring } from '../../state/authoring';
import { consoleActions } from '../../state/store';
import { MenuButton } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './project.module.css';

// 当前技能编译时使用的产品内容；产品有新修订时需要重新选择。
export function ProductScope() {
  const [products, setProducts] = useState<AuthoringProduct[] | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const product = useAuthoring(state => authoringDocument(state).product);
  useEffect(() => {
    const controller = new AbortController();
    authoringApi.products(controller.signal).then(result => setProducts(result.items), error => {
      if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('产品范围', error) });
    });
    return () => controller.abort();
  }, []);
  const current = products?.find(item => item.value === product?.value);
  return <>
    <MenuButton label="选择产品范围" caret entries={[
      { label: '不选择产品', onSelect: () => authoringActions.selectProduct(null) },
      ...(products ?? []).map(item => ({ label: item.name, tip: item.source,
        onSelect: () => authoringActions.selectProduct({ value: item.value, revision: item.revision }) })),
    ]}>{product === null ? '未选择' : current?.name ?? '找不到原来选的产品'}</MenuButton>
    {current && product && current.revision !== product.revision && <span className={styles.issue}>产品内容已更新，请重新选择一次。</span>}
    {failure && <ErrorLine {...failure} />}
  </>;
}
