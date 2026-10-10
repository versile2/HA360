# Third-party notices

HA Cartographer's own code is MIT-licensed (see [LICENSE](LICENSE)). This repository and the image `ghcr.io/versile2/ha-cartographer` also contain the components listed here, each under its own licence. This file gives the notices those licences ask for. In the image it sits at `/app/THIRD-PARTY-NOTICES.md`, beside `/app/LICENSE` and the licence texts in `/app/LICENSES/`.

`realm/icon.png` and `realm/logo.png` are original artwork created for this project (not derived from third-party artwork) and are covered by the project's MIT licence.

Versions are those of the packages and files the image is built from (`Directory.Packages.props`, the vendored files). The licence texts below are copied unchanged from the sources named with them. Development and test tools (npm dev dependencies, test NuGet packages) are not part of the image and are not listed.

## Summary

| Component | Version | Licence | Where it is |
|---|---|---|---|
| HA Cartographer (this project) | 0.3.1 | MIT | [LICENSE](LICENSE) |
| MapLibre GL JS | 6.11.2 | BSD-3-Clause (its licence file also covers mapbox-gl-js up to v1.13, glfx.js and d3-color) | `src/Realm.Web/wwwroot/lib/maplibre-gl/`, image |
| Material Design icons (six glyphs) | n/a | Apache-2.0 | `src/Realm.Web/wwwroot/js/realmMap.js`, image |
| Material Design icons (the History screen: stay and end-of-day markers) | n/a | Apache-2.0 | `src/Realm.Web/wwwroot/js/historyMap.js`, image |
| Cinzel | `@fontsource-variable/cinzel` 5.3.0 | SIL OFL 1.1 | `src/Realm.Web/wwwroot/fonts/`, image |
| Atkinson Hyperlegible | `@fontsource/atkinson-hyperlegible` 5.3.0 | SIL OFL 1.1 | `src/Realm.Web/wwwroot/fonts/`, image |
| MudBlazor | 9.5.0 | MIT | NuGet package in the image |
| MudX.MudBlazor.Extension | 9.5.0 | MIT | NuGet package in the image |
| Microsoft.EntityFrameworkCore.Sqlite, EF Core, Microsoft.Data.Sqlite, Microsoft.Extensions.* | 10.0.12 | MIT | NuGet packages in the image |
| SQLitePCLRaw | 2.1.12 | Apache-2.0 | NuGet packages in the image |
| SQLite (native library) | via SQLitePCLRaw | public domain | NuGet package in the image |
| .NET runtime, ASP.NET Core | 10.0 | MIT | base image |
| Base image `mcr.microsoft.com/dotnet/aspnet` | 10.0 (Ubuntu 24.04) | per package | base image |
| OpenFreeMap, OpenMapTiles, OpenStreetMap, USGS The National Map | live services | see the last section | fetched by the browser, not distributed |

## Bundled files

### MapLibre GL JS 6.11.2 (BSD-3-Clause)

Vendored unchanged in `src/Realm.Web/wwwroot/lib/maplibre-gl/` (`tools/vendor-maplibre.sh` verifies the files against the npm package `maplibre-gl@6.11.2`). The text below is that folder's `LICENSE.txt`, copied unchanged; it is also the licence of the code from mapbox-gl-js v1.13 and earlier, glfx.js and d3-color that MapLibre contains. The name MapLibre GL JS is not used here to endorse or promote HA Cartographer.

~~~~text
Copyright (c) 2023, MapLibre contributors

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright notice,
      this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright notice,
      this list of conditions and the following disclaimer in the documentation
      and/or other materials provided with the distribution.
    * Neither the name of MapLibre GL JS nor the names of its contributors
      may be used to endorse or promote products derived from this software
      without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


-------------------------------------------------------------------------------

Contains code from mapbox-gl-js v1.13 and earlier

Version v1.13 of mapbox-gl-js and earlier are licensed under a BSD-3-Clause license

Copyright (c) 2020, Mapbox
Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice,
  this list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.
* Neither the name of Mapbox GL JS nor the names of its contributors
  may be used to endorse or promote products derived from this software
  without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


-------------------------------------------------------------------------------

Contains code from glfx.js

Copyright (C) 2011 by Evan Wallace

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

--------------------------------------------------------------------------------

Contains a portion of d3-color https://github.com/d3/d3-color

Copyright 2010-2016 Mike Bostock
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of the author nor the names of contributors may be used to
  endorse or promote products derived from this software without specific prior
  written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
~~~~

### Material Design icons (Apache-2.0)

Material Design icons by Google, <https://github.com/google/material-design-icons>, licensed under the Apache License, Version 2.0. Six glyphs are copied as SVG path data into `src/Realm.Web/wwwroot/js/realmMap.js` (the `ICONS` table): `directions_car` (key `car`), `schedule`, `cloud_off` (key `cloudOff`), `home`, `battery_alert` (key `batteryAlert`) and `place`. The pickup truck glyph and the pin tip in the same file are original work under this project's MIT licence. The full Apache License 2.0 text is in [LICENSES/Apache-2.0.txt](LICENSES/Apache-2.0.txt).

MudBlazor's `Icons.Material.*` constants, which the Razor components use, are Material Design icons as well; they ship inside the MudBlazor package (below).

### Fonts: Cinzel and Atkinson Hyperlegible (SIL Open Font License 1.1)

Self-hosted as Latin subsets in `src/Realm.Web/wwwroot/fonts/` (`tools/fonts/update-fonts.sh` verifies the files against the npm packages). Each font's licence text, with its copyright line, sits beside the font files and is served and shipped with them:

- Cinzel (variable weight): Copyright 2020 The Cinzel Project Authors (<https://github.com/NDISCOVER/Cinzel>). Licence text: `src/Realm.Web/wwwroot/fonts/OFL-Cinzel.txt`.
- Atkinson Hyperlegible (regular and bold): Copyright 2020 Braille Institute of America, Inc. Licence text: `src/Realm.Web/wwwroot/fonts/OFL-AtkinsonHyperlegible.txt`.

Neither copyright line declares a Reserved Font Name. The fonts are bundled with the app and are not sold on their own.

## NuGet packages in the image

### MudBlazor 9.5.0 (MIT)

Source: the `LICENSE` file of <https://github.com/MudBlazor/MudBlazor> at tag `v9.5.0`.

~~~~text
MIT License

Copyright (c) 2021 MudBlazor

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
~~~~

### MudX.MudBlazor.Extension 9.5.0 (MIT)

Source: the `LICENSE` file of <https://github.com/MudXtra/MudX> at tag `v9.5.0`.

~~~~text
MIT License

Copyright (c) 2025 MudXtra / MudX

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
~~~~

### Microsoft.EntityFrameworkCore.Sqlite 10.0.12, EF Core, Microsoft.Data.Sqlite and Microsoft.Extensions.* (MIT)

Copyright (c) .NET Foundation and Contributors. Source: the `LICENSE.txt` file of <https://github.com/dotnet/efcore> at tag `v10.0.12`. The same text is the licence file of <https://github.com/dotnet/aspnetcore> and <https://github.com/dotnet/runtime> (checked at tag `v10.0.12`), which cover ASP.NET Core (including `_framework/blazor.web.js`), the Microsoft.Extensions.* packages and the .NET runtime.

~~~~text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

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
~~~~

### SQLitePCLRaw 2.1.12 (Apache-2.0)

Copyright SourceGear (formerly Zumero). SQLitePCLRaw is the native SQLite binding that Microsoft.Data.Sqlite uses; Microsoft.EntityFrameworkCore.Sqlite 10.0.12 depends on `SQLitePCLRaw.bundle_e_sqlite3` and its packages, at version 2.1.12 (`eng/Versions.props` of the efcore repository at tag `v10.0.12`). It is licensed under the Apache License, Version 2.0; the full text is in [LICENSES/Apache-2.0.txt](LICENSES/Apache-2.0.txt).

Its `NOTICE.TXT` follows, copied unchanged from <https://github.com/ericsink/SQLitePCL.raw> at tag `v2.1.12`:

~~~~text

----------------------------------------------------------------
Copyright on SQLitePCL.raw
----------------------------------------------------------------

Version prior to 2.0 were labeled with the copyright owned by
Zumero.  In 2.0, this changed to SourceGear.  There is no legal
distinction, as Zumero is simply a dba name for SourceGear.

And in either case, the open source license remains the same,
Apache v2.

----------------------------------------------------------------
License for SQLite
----------------------------------------------------------------

** The author disclaims copyright to this source code.  In place of
** a legal notice, here is a blessing:
**
**    May you do good and not evil.
**    May you find forgiveness for yourself and forgive others.
**    May you share freely, never taking more than you give.
**


----------------------------------------------------------------
License for MS Open Tech
----------------------------------------------------------------

// Copyright © Microsoft Open Technologies, Inc.
// All Rights Reserved
// Licensed under the Apache License, Version 2.0 (the "License"); you may not
// use this file except in compliance with the License. You may obtain a copy
// of the License at 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// THIS CODE IS PROVIDED ON AN *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
// 
// See the Apache 2 License for the specific language governing permissions and
// limitations under the License.


----------------------------------------------------------------
License for SQLCipher
----------------------------------------------------------------

Copyright (c) 2008, ZETETIC LLC
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.
    * Neither the name of the ZETETIC LLC nor the
      names of its contributors may be used to endorse or promote products
      derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY ZETETIC LLC ''AS IS'' AND ANY
EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL ZETETIC LLC BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


----------------------------------------------------------------
License for OpenSSL libcrypto
----------------------------------------------------------------

  LICENSE ISSUES
  ==============

  The OpenSSL toolkit stays under a dual license, i.e. both the conditions of
  the OpenSSL License and the original SSLeay license apply to the toolkit.
  See below for the actual license texts. Actually both licenses are BSD-style
  Open Source licenses. In case of any license issues related to OpenSSL
  please contact openssl-core@openssl.org.

  OpenSSL License
  ---------------

/* ====================================================================
 * Copyright (c) 1998-2011 The OpenSSL Project.  All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 *
 * 1. Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer. 
 *
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in
 *    the documentation and/or other materials provided with the
 *    distribution.
 *
 * 3. All advertising materials mentioning features or use of this
 *    software must display the following acknowledgment:
 *    "This product includes software developed by the OpenSSL Project
 *    for use in the OpenSSL Toolkit. (http://www.openssl.org/)"
 *
 * 4. The names "OpenSSL Toolkit" and "OpenSSL Project" must not be used to
 *    endorse or promote products derived from this software without
 *    prior written permission. For written permission, please contact
 *    openssl-core@openssl.org.
 *
 * 5. Products derived from this software may not be called "OpenSSL"
 *    nor may "OpenSSL" appear in their names without prior written
 *    permission of the OpenSSL Project.
 *
 * 6. Redistributions of any form whatsoever must retain the following
 *    acknowledgment:
 *    "This product includes software developed by the OpenSSL Project
 *    for use in the OpenSSL Toolkit (http://www.openssl.org/)"
 *
 * THIS SOFTWARE IS PROVIDED BY THE OpenSSL PROJECT ``AS IS'' AND ANY
 * EXPRESSED OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
 * PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE OpenSSL PROJECT OR
 * ITS CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
 * HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED
 * OF THE POSSIBILITY OF SUCH DAMAGE.
 * ====================================================================
 *
 * This product includes cryptographic software written by Eric Young
 * (eay@cryptsoft.com).  This product includes software written by Tim
 * Hudson (tjh@cryptsoft.com).
 *
 */

 Original SSLeay License
 -----------------------

/* Copyright (C) 1995-1998 Eric Young (eay@cryptsoft.com)
 * All rights reserved.
 *
 * This package is an SSL implementation written
 * by Eric Young (eay@cryptsoft.com).
 * The implementation was written so as to conform with Netscapes SSL.
 * 
 * This library is free for commercial and non-commercial use as long as
 * the following conditions are aheared to.  The following conditions
 * apply to all code found in this distribution, be it the RC4, RSA,
 * lhash, DES, etc., code; not just the SSL code.  The SSL documentation
 * included with this distribution is covered by the same copyright terms
 * except that the holder is Tim Hudson (tjh@cryptsoft.com).
 * 
 * Copyright remains Eric Young's, and as such any Copyright notices in
 * the code are not to be removed.
 * If this package is used in a product, Eric Young should be given attribution
 * as the author of the parts of the library used.
 * This can be in the form of a textual message at program startup or
 * in documentation (online or textual) provided with the package.
 * 
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 * 1. Redistributions of source code must retain the copyright
 *    notice, this list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 * 3. All advertising materials mentioning features or use of this software
 *    must display the following acknowledgement:
 *    "This product includes cryptographic software written by
 *     Eric Young (eay@cryptsoft.com)"
 *    The word 'cryptographic' can be left out if the rouines from the library
 *    being used are not cryptographic related :-).
 * 4. If you include any Windows specific code (or a derivative thereof) from 
 *    the apps directory (application code) you must include an acknowledgement:
 *    "This product includes software written by Tim Hudson (tjh@cryptsoft.com)"
 * 
 * THIS SOFTWARE IS PROVIDED BY ERIC YOUNG ``AS IS'' AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED.  IN NO EVENT SHALL THE AUTHOR OR CONTRIBUTORS BE LIABLE
 * FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
 * DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS
 * OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
 * HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
 * LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY
 * OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF
 * SUCH DAMAGE.
 * 
 * The licence and distribution terms for any publically available version or
 * derivative of this code cannot be changed.  i.e. this code cannot simply be
 * copied and put under another distribution licence
 * [including the GNU Public Licence.]
 */


~~~~

### SQLite

The native SQLite library `libe_sqlite3.so` comes with the SQLitePCLRaw packages. SQLite is in the public domain (<https://www.sqlite.org/copyright.html>); its blessing is quoted in the SQLitePCLRaw notice above.

## Base image

The image is built on `mcr.microsoft.com/dotnet/aspnet:10.0` (Ubuntu 24.04). The .NET runtime and ASP.NET Core in it are MIT-licensed (Copyright (c) .NET Foundation and Contributors); the base image carries `/usr/share/dotnet/LICENSE.txt` and `ThirdPartyNotices.txt`, which this project leaves intact. The Ubuntu packages are under their own licences: each package's copyright file is in `/usr/share/doc/<package>/copyright` inside the image, and the sources are in the Ubuntu archive.

## Fetched at run time, not distributed

The browser, not the app, fetches the map tiles, styles, fonts and symbols below from public services; none of it is stored in the repository or the image. The map shows the credits for these sources in its (i) control, open when the map loads.

- **OpenStreetMap.** Map data © OpenStreetMap contributors, available under the Open Database License (ODbL) 1.0. See <https://www.openstreetmap.org/copyright>.
- **OpenFreeMap** (<https://openfreemap.org>) serves the Night (`dark`), Day (`positron`) and Streets (`liberty`) styles and their tiles from its public instance. Its attribution is "OpenFreeMap © OpenMapTiles Data from OpenStreetMap" (the OpenFreeMap part is optional), with links to <https://openfreemap.org>, <https://www.openmaptiles.org/> and <https://www.openstreetmap.org/copyright>. The openfreemap-styles repository is MIT-licensed (Copyright (c) 2023 Zsolt Ero).
- **OpenMapTiles** (<https://openmaptiles.org>): the tile schema and the style designs are CC BY 4.0, its code BSD-3-Clause.
- **Style forks** (from the openfreemap-styles README and the licence files of the upstream styles): Dark comes from `openmaptiles/dark-matter-gl-style` and Positron from `openmaptiles/positron-gl-style`, both derived from CartoDB Basemaps, designed by Stamen and Paul Norman for CartoDB Inc., licensed CC BY 3.0. Liberty comes from `maputnik/osm-liberty`, which is derived from OSM Bright of Mapbox Open Styles.
- **Fonts, icons and data inside those styles:** Noto Sans (SIL OFL 1.1), the Maki icon set (CC0 1.0), Natural Earth (public domain).
- **USGS The National Map.** The Satellite style shows USGS Imagery Only, a U.S. Government work in the public domain. The map credits it as "USGS The National Map"; the U.S. Geological Survey asks for credit, see <https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits>.
