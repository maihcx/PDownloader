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

global using PDownloader.CFS;
global using PDownloader.Core.Download;
global using PDownloader.Core.Download.Exceptions;
global using PDownloader.Core.Download.Handlers;
global using PDownloader.Core.Download.Hls;
global using PDownloader.Core.Download.Http;
global using PDownloader.Core.Download.Infrastructure;
global using PDownloader.Core.Download.Media;
global using PDownloader.Core.Download.Models;
global using PDownloader.Core.Download.Segments;
global using PDownloader.Core.Models;
global using PDownloader.Core.Runtime;
global using PDownloader.Core.Service;
global using PDownloader.Core.Utils;
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.IO;
global using System.Linq;
global using System.Net;
global using System.Net.Http;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Tasks;
