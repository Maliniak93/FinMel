// Market-data stub for the e2e stack: impersonates Yahoo, NBP and the MF bond page from e2e/market-data/data.json.
// Every price is constant and every date is relative to today (UTC), so the data set never goes stale.

import { createServer } from 'node:http';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const DIR = path.dirname(fileURLToPath(import.meta.url));
// Next to the script in the image, one level up in the repo.
const DATA_DIR = [path.join(DIR, 'market-data'), path.join(DIR, '..', 'market-data')].find((d) =>
  existsSync(d),
);
const data = JSON.parse(readFileSync(path.join(DATA_DIR, 'data.json'), 'utf8'));
const bondFile = readFileSync(path.join(DATA_DIR, data.bondFile));
const PORT = Number(process.env.PORT ?? 8080);
const DAY_MS = 24 * 60 * 60 * 1000;
const NBP_MAX_RANGE_DAYS = 93;

const requests = [];

const isoDate = (d) => d.toISOString().slice(0, 10);
const isWeekday = (d) => d.getUTCDay() !== 0 && d.getUTCDay() !== 6;
const midnight = (d) => new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()));
const today = () => midnight(new Date());

function weekdaysBetween(from, to) {
  const days = [];
  for (let d = midnight(from); d <= to; d = new Date(d.getTime() + DAY_MS)) {
    if (isWeekday(d)) days.push(d);
  }
  return days;
}

function send(res, status, body, type = 'application/json') {
  res.writeHead(status, { 'Content-Type': type });
  res.end(body);
}

const sendJson = (res, status, value) => send(res, status, JSON.stringify(value));

function yahooChart(res, symbol, searchParams) {
  const listing = data.listings.find((l) => l.symbol.toLowerCase() === symbol.toLowerCase());
  if (!listing) {
    return sendJson(res, 404, {
      chart: {
        result: null,
        error: { code: 'Not Found', description: 'No data found, symbol may be delisted' },
      },
    });
  }

  const now = today();
  let days;
  if (searchParams.has('period1') && searchParams.has('period2')) {
    const from = new Date(Number(searchParams.get('period1')) * 1000);
    const toExclusive = new Date(Number(searchParams.get('period2')) * 1000);
    const last = new Date(Math.min(toExclusive.getTime() - 1, now.getTime()));
    days = weekdaysBetween(from, last);
  } else {
    days = weekdaysBetween(new Date(now.getTime() - 4 * DAY_MS), now);
  }

  // A close is stamped at 12:00 UTC, the same calendar day in every exchange timezone the data set uses.
  const timestamps = days.map((d) => Math.floor((d.getTime() + 12 * 60 * 60 * 1000) / 1000));
  const closes = days.map(() => listing.close).join(',');
  const body =
    `{"chart":{"result":[{"meta":{"currency":"${listing.currency}","symbol":"${listing.symbol}",` +
    `"exchangeName":"${listing.exchange}","exchangeTimezoneName":"Europe/Warsaw","gmtoffset":7200},` +
    `"timestamp":${JSON.stringify(timestamps)},"indicators":{"quote":[{"close":[${closes}]}]}}],"error":null}}`;
  return send(res, 200, body);
}

function yahooSearch(res, query) {
  const needle = query.trim().toLowerCase();
  const quotes = data.listings
    .filter(
      (l) =>
        needle &&
        (l.symbol.toLowerCase().includes(needle) || l.name.toLowerCase().includes(needle)),
    )
    .map((l, i) => ({
      exchange: l.exchange,
      shortname: l.name.toUpperCase(),
      longname: l.name,
      quoteType: l.quoteType,
      symbol: l.symbol,
      index: 'quotes',
      score: 100000 - i,
      isYahooFinance: true,
    }));
  return sendJson(res, 200, { explains: [], count: quotes.length, quotes, news: [] });
}

function nbpTable(date) {
  const rates = Object.entries(data.fx).map(
    ([code, mid]) => `{"currency":"${code}","code":"${code}","mid":${mid}}`,
  );
  const no = `${isoDate(date)}/A/NBP/${date.getUTCFullYear()}`;
  return `{"table":"A","no":"${no}","effectiveDate":"${isoDate(date)}","rates":[${rates.join(',')}]}`;
}

function nbpTables(res, segments) {
  const now = today();
  const parse = (s) => (/^\d{4}-\d{2}-\d{2}$/.test(s) ? new Date(`${s}T00:00:00Z`) : null);

  if (segments.length === 0) {
    const latest = weekdaysBetween(new Date(now.getTime() - 6 * DAY_MS), now).at(-1);
    return send(res, 200, `[${nbpTable(latest)}]`);
  }

  const from = parse(segments[0]);
  const to = segments.length > 1 ? parse(segments[1]) : from;
  if (!from || !to) return send(res, 400, '400 BadRequest - Invalid date', 'text/plain');
  if (to.getTime() - from.getTime() > (NBP_MAX_RANGE_DAYS - 1) * DAY_MS) {
    return send(res, 400, '400 BadRequest - Limit of 93 days has been exceeded', 'text/plain');
  }

  const days = weekdaysBetween(from, new Date(Math.min(to.getTime(), now.getTime())));
  if (days.length === 0)
    return send(res, 404, '404 NotFound - Not Found - Brak danych', 'text/plain');
  return send(res, 200, `[${days.map(nbpTable).join(',')}]`);
}

function mfPage(res) {
  const html = `<html><body><h3>Materials</h3>
<a class="file-download" href="/mf/file/attachment-1" target="_blank" download aria-label="download">
Bond data<br/>
<span class="extension">${data.bondFileName}</span>
<span class="details">1MB</span>
</a>
</body></html>`;
  return send(res, 200, html, 'text/html; charset=utf-8');
}

function route(req, res) {
  const url = new URL(req.url ?? '/', 'http://stub');
  const segments = url.pathname.split('/').filter(Boolean).map(decodeURIComponent);

  if (url.pathname === '/health') return send(res, 200, 'ok', 'text/plain');
  if (url.pathname === '/__requests') {
    if (req.method === 'DELETE') {
      requests.length = 0;
      return send(res, 204, '');
    }
    return sendJson(res, 200, requests);
  }

  requests.push({
    at: new Date().toISOString(),
    method: req.method,
    provider: segments[0] ?? '',
    path: url.pathname,
    query: url.search,
  });

  const [provider, ...rest] = segments;
  if (provider === 'yahoo' && rest[0] === 'v8' && rest[2] === 'chart' && rest[3]) {
    return yahooChart(res, rest[3], url.searchParams);
  }
  if (provider === 'yahoo' && rest[0] === 'v1' && rest[2] === 'search') {
    return yahooSearch(res, url.searchParams.get('q') ?? '');
  }
  if (
    provider === 'nbp' &&
    rest[0] === 'api' &&
    rest[1] === 'exchangerates' &&
    rest[2] === 'tables' &&
    rest[3] === 'A'
  ) {
    return nbpTables(res, rest.slice(4));
  }
  if (provider === 'mf' && rest[0] === 'page') return mfPage(res);
  if (provider === 'mf' && rest[0] === 'file')
    return send(res, 200, bondFile, 'application/vnd.ms-excel');

  return send(res, 404, 'not found', 'text/plain');
}

createServer(route).listen(PORT, () => console.log(`market-stub listening on :${PORT}`));
