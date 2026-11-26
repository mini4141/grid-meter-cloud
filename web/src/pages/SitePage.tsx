import { LineChart, SimpleBarChart } from '@carbon/charts-react';
import '@carbon/charts-react/styles.css';
import { Button, FileUploaderButton, InlineLoading, InlineNotification, Tag, TextInput, Tile } from '@carbon/react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import type { ApiClient } from '../api/client';
import type { ConsumptionDto, DemandResponseDto, EstimateDto, SiteDetailDto } from '../api/generated/models';

export function SitePage({ api }: { api: ApiClient }) {
  const { id = '' } = useParams();
  const site = api.api.sites.byId(id);
  const [detail, setDetail] = useState<SiteDetailDto | null>(null);
  const [consumption, setConsumption] = useState<ConsumptionDto | null>(null);
  const [estimate, setEstimate] = useState<EstimateDto | null>(null);
  const [estimateError, setEstimateError] = useState('');
  const [flagOn, setFlagOn] = useState(false);
  const [drs, setDrs] = useState<DemandResponseDto[]>([]);
  const [meterId, setMeterId] = useState('');
  const [status, setStatus] = useState('');
  const [processing, setProcessing] = useState(false);
  const [targetKw, setTargetKw] = useState('150');
  const lastTotal = useRef(-1);
  const stableTicks = useRef(0);

  const loadAll = useCallback(async () => {
    setDetail((await api.api.sites.byId(id).get()) ?? null);
    const c = (await api.api.sites.byId(id).consumption.get()) ?? null;
    setConsumption(c);
    setDrs((await api.api.sites.byId(id).demandResponse.get()) ?? []);
    const info = await api.api.admin.info.get();
    setFlagOn(!!info?.tariffEstimates);
    return c;
  }, [api, id]);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);

  // Estimate panel: only when the feature flag is on.
  useEffect(() => {
    if (!flagOn) {
      setEstimate(null);
      return;
    }
    setEstimateError('');
    api.api.sites
      .byId(id)
      .estimate.get()
      .then((e) => setEstimate(e ?? null))
      .catch(() => setEstimateError('Price service unavailable, try again.'));
  }, [api, id, flagOn, consumption?.totalKwh]);

  // After an upload, poll until the ingested total stops growing.
  useEffect(() => {
    if (!processing) return;
    const timer = setInterval(async () => {
      const c = await loadAll();
      const total = c?.totalKwh ?? 0;
      stableTicks.current = total === lastTotal.current && total > 0 ? stableTicks.current + 1 : 0;
      lastTotal.current = total;
      if (stableTicks.current >= 3) {
        setProcessing(false);
        setStatus('Readings ingested');
      }
    }, 2500);
    return () => clearInterval(timer);
  }, [processing, loadAll]);

  const addMeter = async () => {
    await site.meters.post({ externalId: meterId, type: 'Import' });
    setMeterId('');
    await loadAll();
  };

  const upload = async (e: { target: { files?: FileList | null } }) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setStatus('Uploading…');
    const ticket = await api.api.uploads.post({ fileName: file.name });
    const put = await fetch(ticket!.url!, { method: 'PUT', headers: { 'Content-Type': 'text/csv' }, body: file });
    if (!put.ok) {
      setStatus('Upload failed');
      return;
    }
    lastTotal.current = -1;
    stableTicks.current = 0;
    setStatus('Uploaded. The parser Lambda is processing the file…');
    setProcessing(true);
  };

  const issueDr = async () => {
    const start = new Date();
    start.setUTCHours(17, 0, 0, 0);
    await site.demandResponse.post({ windowStart: start, windowEnd: new Date(start.getTime() + 2 * 3600_000), targetKw: Number(targetKw) });
    await loadAll();
    setTimeout(() => void loadAll(), 4000);
  };

  const dailyData = (consumption?.daily ?? []).map((p) => ({
    group: 'Daily kWh',
    date: p.day ?? new Date(),
    value: Number(p.kwh ?? 0),
  }));
  const meterData = (consumption?.perMeter ?? []).map((m) => ({ group: m.meterId ?? '', value: Number(m.kwh ?? 0) }));

  return (
    <>
      <h1>{detail?.name ?? 'Site'}</h1>
      <p>{detail?.region}</p>

      <div className="section">
        <h3>Meters</h3>
        <div className="row">
          {(detail?.meters ?? []).map((m) => (
            <Tag key={m.id} type="gray">
              {m.externalId}
            </Tag>
          ))}
        </div>
        <div className="row">
          <TextInput id="meter-id" labelText="Meter ID (MPAN)" value={meterId} onChange={(e) => setMeterId(e.target.value)} />
          <Button onClick={addMeter} disabled={!meterId}>
            Add meter
          </Button>
        </div>
      </div>

      <div className="section">
        <h3>Upload meter readings</h3>
        <div className="row">
          <FileUploaderButton
            labelText="Select CSV"
            buttonKind="tertiary"
            accept={['.csv']}
            disableLabelChanges
            onChange={upload}
            name="readings"
          />
          {processing ? <InlineLoading description={status} /> : <span data-testid="upload-status">{status}</span>}
        </div>
      </div>

      <div className="section">
        <h3>Consumption</h3>
        <div className="tiles">
          <Tile>
            Total
            <div className="tile-value" data-testid="total-kwh">
              {Number(consumption?.totalKwh ?? 0).toFixed(1)} kWh
            </div>
          </Tile>
          <Tile>
            Peak demand
            <div className="tile-value" data-testid="peak-kw">
              {Number(consumption?.peakKw ?? 0).toFixed(2)} kW
            </div>
          </Tile>
        </div>
        {dailyData.length > 0 && (
          <div className="section" data-testid="consumption-chart">
            <LineChart
              data={dailyData}
              options={{
                title: 'Daily consumption (kWh)',
                axes: {
                  bottom: { mapsTo: 'date', scaleType: 'time' as never },
                  left: { mapsTo: 'value', title: 'kWh' },
                },
                height: '320px',
              }}
            />
          </div>
        )}
        {meterData.length > 0 && (
          <SimpleBarChart
            data={meterData}
            options={{
              title: 'Consumption per meter (kWh)',
              axes: { left: { mapsTo: 'group', scaleType: 'labels' as never }, bottom: { mapsTo: 'value' } },
              height: '240px',
            }}
          />
        )}
      </div>

      {flagOn && (
        <div className="section" data-testid="estimate-panel">
          <h3>Bill estimate</h3>
          {estimate && (
            <Tile>
              Estimated cost at wholesale {estimate.pricePerKwh} per kWh
              <div className="tile-value" data-testid="estimate-cost">
                {estimate.currency} {Number(estimate.estimatedCost).toFixed(2)}
              </div>
            </Tile>
          )}
          {estimateError && <InlineNotification kind="warning" title={estimateError} hideCloseButton />}
        </div>
      )}

      <div className="section">
        <h3>Demand response</h3>
        <div className="row">
          <TextInput id="target-kw" labelText="Target kW reduction" value={targetKw} onChange={(e) => setTargetKw(e.target.value)} />
          <Button onClick={issueDr}>Issue demand-response event</Button>
        </div>
        <div className="row" data-testid="dr-list">
          {drs.map((d) => (
            <Tag key={d.id} type={d.published ? 'green' : 'gray'}>
              {Number(d.targetKw)} kW · {d.published ? 'published via outbox' : 'pending'}
            </Tag>
          ))}
        </div>
      </div>
    </>
  );
}
