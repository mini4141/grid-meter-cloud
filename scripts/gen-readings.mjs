// Generates a month of half-hourly readings for the given meters: node gen-readings.mjs out.csv MPAN-1 MPAN-2
const [out, ...meters] = process.argv.slice(2);
import { writeFileSync } from 'node:fs';
const lines = ['meter_id,timestamp,kwh'];
const start = Date.UTC(2026, 10, 1);
for (const m of meters) {
  const base = 0.3 + (m.length % 3) * 0.1;
  for (let i = 0; i < 30 * 48; i++) {
    const t = new Date(start + i * 30 * 60 * 1000);
    const hour = t.getUTCHours() + t.getUTCMinutes() / 60;
    const evening = Math.exp(-((hour - 18) ** 2) / 8) * 0.6;
    const kwh = (base + evening + ((i * 7919) % 13) / 100).toFixed(3);
    lines.push(`${m},${t.toISOString()},${kwh}`);
  }
}
writeFileSync(out, lines.join('\n') + '\n');
