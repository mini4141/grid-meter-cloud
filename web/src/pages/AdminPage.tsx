import { StructuredListBody, StructuredListCell, StructuredListRow, StructuredListWrapper, Toggle } from '@carbon/react';
import { useCallback, useEffect, useState } from 'react';
import type { ApiClient } from '../api/client';
import type { AdminInfo } from '../api/generated/models';

export function AdminPage({ api }: { api: ApiClient }) {
  const [info, setInfo] = useState<AdminInfo | null>(null);

  const load = useCallback(async () => setInfo((await api.api.admin.info.get()) ?? null), [api]);
  useEffect(() => {
    void load();
  }, [load]);

  const toggle = async (enabled: boolean) => {
    await api.api.admin.flags.tariffEstimates.put({ enabled });
    await load();
  };

  if (!info) return <p>Loading…</p>;
  return (
    <>
      <h1>Admin</h1>
      <StructuredListWrapper aria-label="Build info">
        <StructuredListBody>
          <StructuredListRow>
            <StructuredListCell noWrap>Version</StructuredListCell>
            <StructuredListCell data-testid="build-version">{info.version}</StructuredListCell>
          </StructuredListRow>
          <StructuredListRow>
            <StructuredListCell noWrap>Environment</StructuredListCell>
            <StructuredListCell>{info.environment}</StructuredListCell>
          </StructuredListRow>
        </StructuredListBody>
      </StructuredListWrapper>
      <div className="section">
        <Toggle
          id="flag-tariff-estimates"
          labelText="Feature flag: tariff-estimates"
          labelA="Off"
          labelB="On"
          toggled={!!info.tariffEstimates}
          readOnly={!info.canToggleFlags}
          disabled={!info.canToggleFlags}
          onToggle={toggle}
        />
        {!info.canToggleFlags && <p>Flag is managed in AWS AppConfig (read-only here).</p>}
      </div>
    </>
  );
}
