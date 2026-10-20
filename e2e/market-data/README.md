# e2e market data

The data set the stub (`e2e/stub/server.mjs`) serves in place of Yahoo, NBP and the MF bond page. Machine form: `data.json`. Every price is constant over time and every date is relative to today (UTC), so nothing goes stale: the latest quote is the latest weekday, history covers every weekday in the requested range.

## Values

| Item | Provider | Value | Notes |
| --- | --- | --- | --- |
| USD/PLN | NBP table A | 4.0000 | latest, per day and range |
| EUR/PLN | NBP table A | 4.2500 | |
| GBP/PLN | NBP table A | 5.0000 | |
| CHF/PLN | NBP table A | 4.5000 | |
| CDR.WA | Yahoo chart | 100.00 PLN | seeded by MarketData, exchange WSE |
| VWCE.DE | Yahoo chart | 100.00 EUR | seeded by MarketData, exchange GER |
| PKO.WA | Yahoo chart and search | 50.00 PLN | search finds it, the seeder does not |
| GC=F | Yahoo chart | 9331.04304 USD per ounce | seeded gold, MarketData serves 300.00 USD per gram |
| SI=F | Yahoo chart | 93.3104304 USD per ounce | seeded silver, MarketData serves 3.00 USD per gram |
| OTS0127 | MF bond file | first-period rate 2.00 % | from the recorded `mf-bonds.xls`; the file also holds every other series of that recording |
| NODATA.WA | Yahoo chart | no data | any ticker not listed above answers the same way |

The ounce prices are chosen so `MetalQuoteUnit` (division by 31.1034768 grams per troy ounce, rounded to 8 places) yields exactly 300.00000000 and 3.00000000.

## Stub paths

| Provider | Base URL in the e2e stack | Paths |
| --- | --- | --- |
| Yahoo | `Yahoo:BaseUrl` = `http://market-stub:8080/yahoo/` | `v8/finance/chart/{ticker}` (latest `range=5d`, history `period1`/`period2`), `v1/finance/search?q=` |
| NBP | `Nbp:BaseUrl` = `http://market-stub:8080/nbp/api/` | `exchangerates/tables/A/`, `…/A/{date}/`, `…/A/{from}/{to}/` (404 when no weekday in range, 400 above 93 days) |
| MF | `BondCatalog:PageUrl` = `http://market-stub:8080/mf/page` | the page links `mf/file/…`, which serves `mf-bonds.xls` |

`GET /__requests` returns every call as `{ at, method, provider, path, query }`; `DELETE /__requests` clears it.

## Changing the data

Edit `data.json` and this file together. When a provider changes its payload shape, change the recorded response under `services/MarketData/Skarbiec.MarketData.Tests/Fixtures/PriceSources/RecordedResponses/` first, then the stub, so the parsers keep being tested on the real shape.
