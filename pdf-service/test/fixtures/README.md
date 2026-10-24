# PDF content fixtures

Files are local and tests need no network. MF files are official examples
with LF line endings and trailing whitespace removed; their schema-specific
namespaces, names, address structures and values are preserved. Downloaded
from https://www.gov.pl/web/kas/krajowy-system-e-faktur. on 2026-10-24:

| Local file | Official archive / example |
| --- | --- |
| mf-fa1.xml | [FA(1)](https://www.gov.pl/attachment/537c16f8-8ceb-41a3-8928-7ada0a46a0a2), example 1 |
| mf-fa2.xml | [FA(2)](https://www.gov.pl/attachment/4b2a3e22-8ed0-4670-b04a-08cfdf34552d), example 1 |
| mf-fa3.xml | [FA(3)](https://www.gov.pl/attachment/937002fa-c6b5-477d-8b56-22105fa728c2), example 1 |
| mf-fa3-correction.xml | FA(3), example 2 (signed KOR amounts) |
| mf-fa3-advance.xml | FA(3), example 10 (ZAL) |
| mf-fa3-eur.xml | FA(3), example 20 (EUR amounts and VAT in PLN) |
| mf-fa3-zero-correction.xml | FA(3), example 5 (identity correction, P_15 = 0, no rows) |

`upstream-fa3-large.xml` is `assets/invoice.xml` (with the same whitespace normalization) from
[CIRFMF release 1.1.46](https://github.com/CIRFMF/ksef-pdf-generator/blob/8a18843db12d0013a417500bb2f98bfe49b7d2a8/assets/invoice.xml)
(MIT, see `lib/LICENSE`): 13 verbose rows, Polish names, multiple pages.

`fa3-rounding.xml` is a deliberately modified MF FA(3) example 1: no rows,
exemption basis, P_13_7 = P_15 = 100000000000000.03, no taxable subtotals.
This boundary amount exposes a second floating-point rounding in the old tax
summary (incorrectly rendered as 100000000000000.05). It is a synthetic
rendering regression case, not a real transaction or an XSD validation test.

The requested local `ksef-docs` checkout only contains UPO XML examples, not
invoice XML examples; invoice fixtures therefore come from MF and upstream.
The older `invoice-fa3.xml` / `.json` remain small wrapper/error-path fixtures.
