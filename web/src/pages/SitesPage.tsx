import {
  Button,
  DataTable,
  InlineNotification,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
  TextInput,
} from '@carbon/react';
import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type { ApiClient } from '../api/client';
import type { SiteDto } from '../api/generated/models';

const headers = [
  { key: 'name', header: 'Name' },
  { key: 'region', header: 'Region' },
  { key: 'meterCount', header: 'Meters' },
];

export function SitesPage({ api }: { api: ApiClient }) {
  const [sites, setSites] = useState<SiteDto[]>([]);
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [region, setRegion] = useState('');
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setSites((await api.api.sites.get()) ?? []);
  }, [api]);

  useEffect(() => {
    void load();
  }, [load]);

  const create = async () => {
    try {
      await api.api.sites.post({ name, region });
      setOpen(false);
      setName('');
      setRegion('');
      setError('');
      await load();
    } catch {
      setError('Could not create site. Name and region are required.');
    }
  };

  const rows = sites.map((s) => ({ id: s.id ?? '', name: s.name ?? '', region: s.region ?? '', meterCount: s.meterCount ?? 0 }));

  return (
    <>
      <div className="row">
        <h1>Sites</h1>
        <Button onClick={() => setOpen(true)}>Create site</Button>
      </div>
      <div className="section">
        <DataTable rows={rows} headers={headers}>
          {({ rows: tableRows, headers: tableHeaders, getTableProps, getHeaderProps, getRowProps }) => (
            <TableContainer title="Your sites" description="Sites and meters for the signed-in tenant">
              <Table {...getTableProps()}>
                <TableHead>
                  <TableRow>
                    {tableHeaders.map((h) => {
                      const { key, ...rest } = getHeaderProps({ header: h });
                      return (
                        <TableHeader key={key} {...rest}>
                          {h.header}
                        </TableHeader>
                      );
                    })}
                  </TableRow>
                </TableHead>
                <TableBody>
                  {tableRows.map((row) => {
                    const { key, ...rest } = getRowProps({ row });
                    return (
                      <TableRow key={key} {...rest}>
                        {row.cells.map((cell) => (
                          <TableCell key={cell.id}>
                            {cell.info.header === 'name' ? <Link to={`/sites/${row.id}`}>{cell.value}</Link> : cell.value}
                          </TableCell>
                        ))}
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </DataTable>
        {rows.length === 0 && <p>No sites yet.</p>}
      </div>
      <Modal
        open={open}
        modalHeading="Create site"
        primaryButtonText="Create"
        secondaryButtonText="Cancel"
        onRequestClose={() => setOpen(false)}
        onRequestSubmit={create}
      >
        <TextInput id="site-name" labelText="Name" value={name} onChange={(e) => setName(e.target.value)} />
        <TextInput id="site-region" labelText="Region" value={region} onChange={(e) => setRegion(e.target.value)} />
        {error && <InlineNotification kind="error" title={error} hideCloseButton />}
      </Modal>
    </>
  );
}
