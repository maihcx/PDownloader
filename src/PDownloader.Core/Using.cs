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
global using PDownloader.Contracts.Application;
global using PDownloader.Contracts.Ipc;
global using PDownloader.Contracts.Downloads;
global using PDownloader.Contracts.Media;
global using PDownloader.Contracts.Updates;
global using PDownloader.Downloads;
global using PDownloader.Downloads.Models;
global using PDownloader.Infrastructure.ExternalTools.YtDlp;
global using PDownloader.Infrastructure.Downloads;
global using PDownloader.Core.Application.App;
global using PDownloader.Core.Application.Downloads;
global using PDownloader.Core.Ipc;
global using PDownloader.Core.Runtime;
global using PDownloader.Core.Service;
global using PDownloader.Core.Update;
global using PDownloader.Core.Utils;
global using System;
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
