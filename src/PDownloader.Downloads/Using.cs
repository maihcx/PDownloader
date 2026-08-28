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
global using PDownloader.Downloads.Exceptions;
global using PDownloader.Downloads.Handlers;
global using PDownloader.Downloads.Hls;
global using PDownloader.Downloads.Models;
global using PDownloader.Downloads.Paths;
global using PDownloader.Downloads.Segments;
global using PDownloader.Infrastructure.Downloads;
global using PDownloader.Infrastructure.ExternalTools.YtDlp;
global using PDownloader.Infrastructure.Http;
global using PDownloader.Infrastructure.Media;
global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Text.Json;
global using System.Text;
