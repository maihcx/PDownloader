// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

global using PDownloader.Contracts.Downloads;
global using PDownloader.Contracts.Media;
global using PDownloader.Infrastructure.Downloads;
global using PDownloader.Infrastructure.ExternalTools;
global using PDownloader.Infrastructure.ExternalTools.Ffmpeg;
global using PDownloader.Infrastructure.ExternalTools.YtDlp;
global using PDownloader.Infrastructure.Http;
global using PDownloader.Infrastructure.Json;
global using PDownloader.Infrastructure.Media;
global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Net;
global using System.Net.Http;
global using System.Security.Cryptography;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
