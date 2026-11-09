# Third-party notices

KSeF Gateway is distributed under AGPL-3.0; see LICENSE. Third-party components
retain their own licenses. This summary covers the principal runtime components,
not a complete dependency license audit. Package license files remain authoritative;
release SBOMs identify the actual resolved dependencies and versions.

| Component | License | Source |
| --- | --- | --- |
| CIRFMF ksef-pdf-generator | MIT (original LICENSE, despite ISC in package.json) | https://github.com/CIRFMF/ksef-pdf-generator |
| KSeF.Client / KSeF.Client.ClientFactory / KSeF.Client.Core 2.9.0.1 | MIT | https://github.com/CIRFMF/ksef-client-csharp/tree/2.9.0.1 |
| .NET / ASP.NET Core, Microsoft.AspNetCore.OpenApi | MIT | https://github.com/dotnet/aspnetcore |
| Scalar.AspNetCore | MIT | https://github.com/scalar/scalar |
| Amazon.Lambda.AspNetCoreServer.Hosting | Apache-2.0 | https://github.com/aws/aws-lambda-dotnet |
| Node.js | MIT and bundled third-party licenses | https://github.com/nodejs/node/blob/main/LICENSE |
| Express | MIT | https://github.com/expressjs/express |
| pdfmake | MIT | https://github.com/bpampuch/pdfmake |
| i18next | MIT | https://github.com/i18next/i18next |
| xml-js | MIT | https://github.com/nashwaan/xml-js |
| tsx | MIT | https://github.com/privatenumber/tsx |

Container OS packages carry their respective licenses, available in the image's
/usr/share/doc directory. The PDF image also preserves the generator's original
LICENSE at /app/licenses/ksef-pdf-generator-LICENSE. The files beside this notice
are copies of the repository-root LICENSE and THIRD_PARTY_NOTICES.md, duplicated
for the PDF Docker build context; keep them
in sync when updating notices.

## ksef-pdf-generator — original LICENSE

```text
MIT License

Copyright (c) 2025 CIRF

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## KSeF.Client SDK 2.9.0.1 — original LICENCE.txt (MIT)

```text
Copyright (c) 2025 Ministerstwo Finansów
 
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Vendored KSeF SDK package SHA-256

```text
1f02fd9cf5440d3eff718dcdff615598f2bd0535d1c721bfe00f7740f591e72f  ksef.client.core.2.9.0.1.nupkg
f1cf030d7e4743ab4dd7c4885edbf851d7f50ec2d5ba00c4933d0fa509826b0f  ksef.client.2.9.0.1.nupkg
7dbb88ec62fb05263955b091726859cd1e4b835c45f757664cb2b72e152598fb  ksef.client.clientfactory.2.9.0.1.nupkg
```
