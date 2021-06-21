using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DeepDreamGames
{
	public class Application
	{
		private class PackageInfo
		{
			public string name;
			public Dictionary<string, Dictionary<string, object>> versions = new Dictionary<string, Dictionary<string, object>>(keyComparer); // SemVer -> package.json
			public string latest; // SemVer of the latest version
			public Dictionary<string, string> time = new Dictionary<string, string>(keyComparer);
		}

		public enum LogType
		{
			None = 0,
			Exception = 1,
			Error = 2,
			Warning = 3,
			Log = 4,
			Info = 5,
			Debug = 6,
		}

		#region Constants
		static private readonly string serverVersion = "1.0.0";
		private const ConsoleColor promptColor = ConsoleColor.Blue;
		private const ConsoleColor defaultColor = ConsoleColor.Gray;
		private const string prompt = "> ";
		static private readonly char[] commandSeparators = new char[] { ' ' };
		static private readonly StringComparer keyComparer = StringComparer.OrdinalIgnoreCase;
		private const StringComparison urlComparison = StringComparison.OrdinalIgnoreCase;
		static private readonly char[] urlSeparator = new char[] { '/' };
		private const string mimeApplicationJson = "application/json";  // Since there is no System.Net.Mime.MediaTypeNames.Application.Json in .NET Framework 4.7.2
		static public readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
		public const long TicksPerSecond = 10000000L;
		#endregion

		#region Config
		static private List<string> prefixes = new List<string>() { "http://localhost/" };
		static private string pathPackages;
		static private List<string> extensions = new List<string>() { ".tgz", ".tar.gz", ".taz" };
		static private LogType verbosity = LogType.Log;
		#endregion

		#region Private Fields
		static private Dictionary<string, PackageInfo> registry = new Dictionary<string, PackageInfo>(keyComparer);	// Package id -> PackageInfo
		static private bool terminated;
		static private HttpListener listener = new HttpListener();
		static private Dictionary<string, Action<string[]>> commands;
		static private JsonReader jsonReader = new JsonReader();
		static private bool hasPrompt;
		#endregion

		#region Main
		// 
		static public void Main(string[] args)
		{
			Log(ConsoleColor.White, "Pachka v{0}\nPackage Registry Server by DeepDreamGames\nhttps://github.com/deepdreamgames/pachka\n", serverVersion);

			#if DEBUG
			Tests.Run();
			#endif

			RegisterCommands();
			ReadConfig(args.Length > 0 ? args[0] : null);
			ParsePackages();
			StartServer();
			ReadInput();
		}

		// 
		static private void ReadConfig(string pathConfig)
		{
			if (string.IsNullOrEmpty(pathConfig)) { pathConfig = "config.json"; }

			string path = Path.GetFullPath(pathConfig).Replace('\\', '/');

			Log("Reading config file at path '{0}'", path);
			if (!File.Exists(path)) { throw new InvalidOperationException(string.Format("config.json is not found at path '{0}'", path)); }

			using (StreamReader streamReader = new StreamReader(path))
			{
				var root = jsonReader.ReadToEnd(streamReader, keyComparer) as Dictionary<string, object>;
				object obj;

				// Endpoints
				if (root.TryGetValue("endpoints", out obj))
				{
					List<object> list = obj as List<object>;
					prefixes.Clear();
					for (int i = 0; i < list.Count; i++)
					{
						string endpoint = (list[i] as string).Trim();
						if (endpoint.Length > 0)
						{
							// Only Uri prefixes ending in '/' are allowed.
							if (endpoint[endpoint.Length - 1] != '/')
							{
								endpoint += '/';
							}
							prefixes.Add(endpoint);
						}
					}
				}

				// Path
				string relativePath = null;
				if (root.TryGetValue("path", out obj))
				{
					relativePath = (string)obj;
				}
				// https://docs.microsoft.com/en-us/dotnet/api/system.io.path.getfullpath?view=netframework-4.7.2
				// Prevent ArgumentException if relativePath is empty. 
				// "path is a zero-length string, contains only white space, or contains one or more of the invalid characters defined in GetInvalidPathChars()."
				if (string.IsNullOrEmpty(relativePath)) { relativePath = "./"; }
				pathPackages = Path.GetFullPath(relativePath).Replace('\\', '/');

				// Extensions
				if (root.TryGetValue("extensions", out obj))
				{
					List<object> list = obj as List<object>;
					extensions.Clear();
					for (int i = 0; i < list.Count; i++)
					{
						string extension = (list[i] as string).Trim();
						if (extension.Length > 0)
						{
							if (extension[0] != '.')
							{
								extension = '.' + extension;
							}
							extensions.Add(extension);
						}
					}
				}
				
				// Verbosity
				if (root.TryGetValue("verbosity", out obj) && obj != null)
				{
					Type enumType = typeof(LogType);
					Array values = Enum.GetValues(enumType);

					Type type = obj.GetType();
					if (type == typeof(string))
					{
						string value = (obj as string).Trim();
						if (value.Length > 0)
						{
							string[] names = Enum.GetNames(enumType);
							for (int i = 0; i < names.Length; i++)
							{
								if (string.Compare(names[i], value, StringComparison.OrdinalIgnoreCase) == 0)
								{
									verbosity = (LogType)values.GetValue(i);
									break;
								}
							}
						}
					}
					else if (type == typeof(long))
					{
						int index = Array.BinarySearch(values, (LogType)(long)obj);
						if (index >= 0)
						{
							verbosity = (LogType)values.GetValue(index);
						}
					}
				}
			}

			Log("* Endpoints: {0}", string.Join(", ", prefixes));
			Log("* Packages Path: {0}", pathPackages);
			Log("* Extensions: {0}", string.Join(", ", extensions));
			Log("* Verbosity level: {0} ({1})", verbosity.ToString(), (int)verbosity);
		}

		// 
		static private void ParsePackages()
		{
			registry.Clear();

			if (!Directory.Exists(pathPackages))
			{
				throw new DirectoryNotFoundException(string.Format("Directory at path '{0}' is not found!", pathPackages));
			}

			Log("Parsing Packages in folder '{0}':", pathPackages);

			const int blockSize = 512;

			Encoding utf8 = Encoding.UTF8;
			byte[] buffer = new byte[65536];    // Don't make it smaller than blockSize
			char[] chars = new char[utf8.GetMaxCharCount(buffer.Length)];
			Decoder decoder = utf8.GetDecoder();
			StringBuilder sb = new StringBuilder();
			Action<int> decodeText = delegate(int length)
			{
				// Decode characters and append them to StringBuilder
				int numChars = decoder.GetChars(buffer, 0, length, chars, 0);
				sb.Append(chars, 0, numChars);
			};
			ExtendedTarHeader extendedHeader = new ExtendedTarHeader();
			StreamWrapper streamWrapper = new StreamWrapper();
			Dictionary<string, string> gRecords = new Dictionary<string, string>();
			Dictionary<string, string> xRecords = new Dictionary<string, string>();
			Decoder valueDecoder = decoder; // TODO Global extended header and extended headers can change value decoder with hdrcharset record, but even ustart doesn't seem to support it

			StringBuilder hashBuilder = new StringBuilder();
			using (SHA1Managed sha1 = new SHA1Managed())
			{
				// Read packages from pathPackages folder
				string[] files = Directory.GetFiles(pathPackages, "*.*");
				foreach (string fullPath in files)
				{
					bool supported = false;
					for (int j = 0; j < extensions.Count; j++)
					{
						string extension = extensions[j];
						if (fullPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
						{
							supported = true;
							break;
						}
					}
					if (!supported) { continue; }

					string fileName = Path.GetFileName(fullPath);
					Log(ConsoleColor.DarkYellow, "* {0}", fileName);

					int numFiles = 0;
					int numDirectories = 0;

					FileInfo fileInfo = new FileInfo(fullPath);
					using (FileStream fileStream = fileInfo.OpenRead())
					{
						Dictionary<string, object> versionInfo = null;
						string packageId = null;
						string version = null;

						// Compute SHA1 hash in advance, so that we will be able to fill package.json shasum field value when we'll be parsing it below
						byte[] data = sha1.ComputeHash(fileStream);
						const string hex = "0123456789abcdef";
						hashBuilder.Length = 0;
						for (int i = 0; i < data.Length; i++)
						{
							byte b = data[i];
							hashBuilder.Append(hex[b >> 4]);
							hashBuilder.Append(hex[b & 0xF]);
						}
						string shasum = hashBuilder.ToString();

						// Extract all necessary information from archive
						fileStream.Seek(0L, SeekOrigin.Begin);
						using (var gzip = new GZipStream(fileStream, CompressionMode.Decompress, false))
						{
							string longName = null;
							while (true)
							{
								// https://www.gnu.org/software/tar/manual/html_node/Standard.html

								// Read header block
								int numRead = gzip.Read(buffer, 0, blockSize);
								if (numRead == 0) { break; }	// EndOfStream
								if (numRead < blockSize)
								{
									Log(LogType.Error, "  Incomplete header block of size {0} found!", numRead);
									break;
								}

								TarHeader header = new TarHeader();

								if (!TarHeader.TryRead(buffer, utf8, header))
								{
									break;  // Will be the case at the end of archive once we reach two 512 blocks filled with 0's
								}

								// Use global header values defined in preceding global extended header ('g' block)
								Apply(header, gRecords);

								// Use extended header values defined in previous extended header ('x' block)
								Apply(header, xRecords);

								// Use longName defined by previous GNU long name header ('L' block)
								if (longName != null)
								{
									//if (IsVerbose(LogType.Debug)) { Log(ConsoleColor.Cyan, "   '{0}' name replaced with\n   '{1}'", header.name, longName); }
									header.name = longName;
								}

								// Reset extended header
								xRecords.Clear();
								longName = null;

								//if (IsVerbose(LogType.Debug)) { Log(header.ToString()); }

								// Content
								long pos = 0;
								switch (header.typeflag)
								{
									// GNU long name
									case (byte)'L':
										{
											if (header.size > 0)
											{
												decoder.Reset();
												sb.Length = 0;

												// GZipStream gzip -> Encoding.UTF8 decoder -> StringBuilder
												Read(gzip, buffer, header.size, decodeText);
												pos += header.size;
												int end = 0;
												while (end < sb.Length && sb[end] != 0) // null-terminated string
												{
													end++;
												}
												longName = sb.ToString(0, end);
											}
										}
										break;

									// Global extended header
									case (byte)'g':
										{
											// If extendedHeader fails - then without StreamWrapper we will lose track of how many bytes were actually read from gzip stream
											streamWrapper.Initialize(gzip, header.size);
											extendedHeader.TryRead(streamWrapper, streamWrapper.Length, valueDecoder, gRecords);
											Log(LogType.Info, "  g [Global extended header]:");
											foreach (var record in gRecords) { Log(LogType.Info, "      {0}={1}", record.Key, record.Value); }
											pos += streamWrapper.Position;
											streamWrapper.Deinitialize();
										}
										break;

									// Extended header referring to the next file in the archive
									case (byte)'x':
										{
											// If extendedHeader fails - then without StreamWrapper we will lose track of how many bytes were actually read from gzip stream
											streamWrapper.Initialize(gzip, header.size);
											extendedHeader.TryRead(streamWrapper, streamWrapper.Length, valueDecoder, xRecords);
											Log(LogType.Info, "  x [Extended header]:");
											foreach (var record in xRecords) { Log(LogType.Info, "      {0}={1}", record.Key, record.Value); }
											pos += streamWrapper.Position;
											streamWrapper.Deinitialize();
										}
										break;

									// Regular file
									case (byte)'0':
									case 0:
										{
											numFiles++;
											Log(LogType.Info, "  0 {0}", header.name);

											// Unpack if necessary
											if (!header.name.Equals("./", StringComparison.InvariantCulture))
											{
												StringComparison nameComparison = StringComparison.OrdinalIgnoreCase;
												const string packagePrefix = "package/";

												// https://docs.unity3d.com/Manual/cus-layout.html
												// package.json
												if (header.name.Equals(packagePrefix + "package.json", nameComparison))
												{
													// GZipStream -> StreamWrapper -> StreamReader -> JsonReader -> Dictionary<string, object>

													// Parse Json from gzip stream and feed it to JsonReader to produce data tree. 
													streamWrapper.Initialize(gzip, header.size);
													using (var streamReader = new StreamReader(streamWrapper, Encoding.UTF8, true, 1024, true))
													{
														// Parse json
														versionInfo = jsonReader.ReadToEnd(streamReader, keyComparer) as Dictionary<string, object>;

														// Extract mandatory fields
														packageId = (string)versionInfo["name"];
														version = (string)versionInfo["version"];

														// Append information about the package
														versionInfo["category"] = string.Empty;
														versionInfo["readmeFilename"] = "README.md";
														versionInfo["_id"] = packageId + '@' + version;
														versionInfo["dist"] = new Dictionary<string, object>()
														{
															{ "shasum", shasum },
															{ "tarball", fileName },
														};

														if (IsVerbose(LogType.Debug))
														{
															Log(ConsoleColor.Cyan, SerializeToJson(versionInfo, true));
														}

														pos += streamWrapper.Position;
													}
												}
												// In npm standard - readmeFilename is defined in package.json https://github.com/npm/registry/blob/master/docs/REGISTRY-API.md#version, 
												// but that would require building tar file tree first and then reading files we're interested in, 
												// so instead - we stick to the Unity's fixed package layout convention https://docs.unity3d.com/Manual/cus-layout.html
												else if (header.name.Equals(packagePrefix + "README.md", nameComparison))
												{
													decoder.Reset();
													sb.Length = 0;

													// GZipStream gzip -> Encoding.UTF8 decoder -> StringBuilder
													Read(gzip, buffer, header.size, decodeText);
													pos += header.size;

													versionInfo["readme"] = sb.ToString();
												}
											}
											else
											{
												Log(LogType.Info, "  - ./");
											}
										}
										break;

									// Directory
									case (byte)'5':
										{
											numDirectories++;
											Log(LogType.Info, "  5 {0}", header.name);
										}
										break;

									default:
										Log(LogType.Warning, "Unhandled typeflag: '{0}' ({1})", header.typeflag >= 32 ? (char)header.typeflag : ' ', header.typeflag);
										break;
								}

								// Tar archive contents is aligned to blocks of 512 bytes. 
								// Adding delta to size will ceil size to 512 bytes. 
								long delta = (blockSize - (header.size % blockSize)) % blockSize;
								Read(gzip, buffer, header.size - pos + delta, null);    // Skips forward in stream
							}
						}

						Log(LogType.Info, "  {0} files {1} directories", numFiles, numDirectories);

						if (string.IsNullOrEmpty(packageId))
						{
							Log(LogType.Error, "  Package id cannot be empty!");
							continue;
						}

						if (string.IsNullOrEmpty(version))
						{
							Log(LogType.Error, "  Version cannot be empty!");
							continue;
						}

						// Get or create PackageInfo
						PackageInfo packageInfo;
						if (!registry.TryGetValue(packageId, out packageInfo))
						{
							// Add new PackageInfo
							packageInfo = new PackageInfo();
							packageInfo.name = packageId;
							registry.Add(packageId, packageInfo);
						}

						// Try to add new version
						if (!packageInfo.versions.ContainsKey(version))
						{
							packageInfo.versions.Add(version, versionInfo);
							// Since we don't publush / unpublish packages - use tgz modification time as published timestamp
							DateTime timePublished = fileInfo.LastWriteTimeUtc;
							// https://dev.mysql.com/doc/refman/8.0/en/date-and-time-types.html
							packageInfo.time.Add(version, timePublished.ToString("yyyy-MM-ddTHH:mm:ssZ"));
						}
						else
						{
							Log(LogType.Error, "  Two different package files report the same version '{0}'!", version);
						}
					}
				}
			}

			// Determine latest package version and exclude invalid versions / packages
			var removePackages = new List<string>();
			foreach (var pair1 in registry)
			{
				PackageInfo packageInfo = pair1.Value;

				SemVer latestSemVer = new SemVer();
				var removeVersions = new List<string>();

				var versions = packageInfo.versions;
				foreach (var pair2 in versions)
				{
					var versionInfo = pair2.Value;

					string error;
					object versionObject;
					if (versionInfo.TryGetValue("version", out versionObject))
					{
						if (versionObject != null)
						{
							Type versionFieldType = versionObject.GetType();
							if (versionFieldType == typeof(string))
							{
								string version = (string)versionObject;
								SemVer semVer;
								if (SemVer.TryParse(version, out semVer))
								{
									if (SemVer.Compare(latestSemVer, semVer) < 0)
									{
										latestSemVer = semVer;
										packageInfo.latest = version;
									}
									continue;
								}
								else
								{
									error = string.Format("Failed to parse '{0}' as valid Semantic Version.", version);
								}
							}
							else
							{
								error = string.Format("package.json version field value '{0}' is of type '{1}', but string type was expected.", versionObject, versionFieldType);
							}
						}
						else
						{
							error = "package.json version field value is null.";
						}
					}
					else
					{
						error = "package.json does not contain version field.";
					}

					removeVersions.Add(pair2.Key);
					string fileName = (string)((Dictionary<string, object>)versionInfo["dist"])["tarball"]; // We know that this field exists because we've added it
					Log(LogType.Error, string.Format("Package version '{0}' was not added. {1}", fileName, error));
				}

				// Remove info about invalid packages
				for (int i = 0; i < removeVersions.Count; i++)
				{
					versions.Remove(removeVersions[i]);
				}

				if (versions.Count == 0)
				{
					string id = pair1.Key;
					Log(LogType.Error, string.Format("Package '{0}' was not added because it contains no valid versions.", id));
					removePackages.Add(id);
				}
			}

			// Remove empty packages
			for (int i = 0; i < removePackages.Count; i++)
			{
				registry.Remove(removePackages[i]);
			}

			// Count versions
			int totalVersions = 0;
			foreach (var pair in registry)
			{
				totalVersions += pair.Value.versions.Count;
			}

			Log(ConsoleColor.Green, "Registered {0} unique packages with {1} versions total.", registry.Count, totalVersions);
		}

		// 
		static private void StartServer()
		{
			if (listener.IsListening)
			{
				Log(LogType.Warning, "Server is already running!");
				return;
			}

			listener.Prefixes.Clear();
			for (int i = 0; i < prefixes.Count; i++)
			{
				listener.Prefixes.Add(prefixes[i]);
			}

			try
			{
				listener.Start();
			}
			catch (HttpListenerException ex)
			{
				// Unhandled Exception: System.Net.HttpListenerException: Access is denied
				Log(LogType.Error, "An exception has occured on attempt to start HttpListener. Try running as an administrator?");
				Log(LogType.Error, ex.ToString());
				return;
			}

			// Log endpoints
			StringBuilder sb = new StringBuilder("Listening for connections on");
			foreach (var prefix in listener.Prefixes)
			{
				if (sb.Length > 0) { sb.Append(' '); }
				sb.Append(prefix);
			}
			Log(sb.ToString());

			// Begin handling requests asynchronously
			Task.Run(ServerLoop);

			Log(ConsoleColor.Green, "Server started.");
		}

		//
		static private void StopServer()
		{
			if (!listener.IsListening)
			{
				Log(LogType.Warning, "Server is already stopped!");
				return;
			}

			listener.Stop();

			Log(ConsoleColor.Green, "Server stopped.");
		}

		// 
		static private async Task ServerLoop()
		{
			while (listener.IsListening)
			{
				try
				{
					var context = await listener.GetContextAsync();
					if (listener.IsListening)
					{
						// Intentionally not using await here - server should be ready to process another request immediately
						#pragma warning disable 4014
						HandleRequest(context);
						#pragma warning restore 4014
					}
				}
				catch (HttpListenerException)
				{
					return; // Listener stopped
				}
			}
		}

		// 
		static private async Task HandleRequest(HttpListenerContext context)
		{
			await FillResponse(context);
			if (IsVerbose(LogType.Info))
			{
				HttpListenerResponse response = context.Response;
				Log(ConsoleColor.DarkYellow, "RESPONSE: StatusCode: {0} ContentType: {1} ContentLength64: {2}", response.StatusCode, response.ContentType, response.ContentLength64);
			}
		}

		// 
		static private async Task FillResponse(HttpListenerContext context)
		{
			HttpListenerRequest request = context.Request;
			HttpListenerResponse response = context.Response;

			Uri url = request.Url;
			string absolutePath = url.AbsolutePath;

			if (IsVerbose(LogType.Info))
			{
				// request.Url, absolutePath, request.HttpMethod, request.UserHostName, request.UserAgent
				Log(ConsoleColor.Cyan, string.Format("REQUEST: {0} {1}", request.HttpMethod, request.Url));
			}

			if (absolutePath != "/favicon.ico") { }

			string[] nodes = absolutePath.Split(urlSeparator, StringSplitOptions.RemoveEmptyEntries);
			int nodeCount = nodes.Length;

			// REST
			if (nodeCount > 0)
			{
				// "/-"
				if (string.Equals(nodes[0], "-", urlComparison))
				{
					if (nodeCount > 1)
					{
						// "/-/v1"
						if (string.Equals(nodes[1], "v1", urlComparison))
						{
							if (nodeCount > 2)
							{
								// "/-/v1/search"
								if (string.Equals(nodes[2], "search", urlComparison))
								{
									await ResponseSearch(response, url.Query);
									return;
								}
							}
						}
					}
				}
				else
				{
					string packageId = nodes[0];
					PackageInfo packageInfo;
					if (registry.TryGetValue(packageId, out packageInfo))
					{
						if (nodeCount > 1)
						{
							// "/{package}/-"
							if (string.Equals(nodes[1], "-", urlComparison))
							{
								// "/{package}/-/{file}"
								if (nodeCount > 2)
								{
									await ResponsePackageFile(response, Uri.UnescapeDataString(nodes[2]), request.HttpMethod);
									return;
								}
							}
							// "/{package}/latest"
							else if (string.Equals(nodes[1], "latest", urlComparison))
							{
								Dictionary<string, object> versionInfo = packageInfo.versions[packageInfo.latest];
								await ResponseVersionMeta(response, versionInfo, GetUrlPrefix(url));
								return;
							}
							// "/{package}/{version}"
							else
							{
								Dictionary<string, object> versionInfo;
								if (packageInfo.versions.TryGetValue(nodes[1], out versionInfo))
								{
									await ResponseVersionMeta(response, versionInfo, GetUrlPrefix(url));
									return;
								}
							}
						}
						// "/{package}"
						else
						{
							await ResponsePackageMeta(response, packageInfo, GetUrlPrefix(url));
							return;
						}
					}
				}

				await ResponseError(response, "Not Found", HttpStatusCode.NotFound);
			}
			else
			{
				await ResponseRoot(response);
			}
		}

		// Search
		static private async Task ResponseSearch(HttpListenerResponse response, string query)
		{
			var parameters = new Dictionary<string, string>(keyComparer);
			ParseQuery(query, parameters);

			string text;
			if (!parameters.TryGetValue("text", out text))
			{
				text = string.Empty;
			}

			string value;

			int from;
			if (!parameters.TryGetValue("from", out value) || !int.TryParse(value, out from) || from < 0)
			{
				from = 0;
			}

			int size;
			if (!parameters.TryGetValue("size", out value) || !int.TryParse(value, out size) || size < 0)
			{
				size = 20;
			}

			// Sanity check
			size = Math.Min(size, 250);

			int total = 0;
			
			// Ring buffer
			PackageInfo[] list = new PackageInfo[size];
			int start = 0;
			int count = 0;

			if (!string.IsNullOrEmpty(text))
			{
				// DB query. Search for `text`, skip `from` number of results and return up to `size` results. 
				foreach (var kvp in registry)
				{
					if (kvp.Key.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						total++;
						// Put result to the buffer even if we're not past `from` number of results yet. 
						// RingBuffer has limited size and we always want to return last `size` number of matches. 
						// E.g. if we have 8 matches total, `from` = 7 and `size` = 5 - instead of returning only 2 matches (index 8 and 9)
						// we want to return last 5 matches (index 5 to 9). 
						if (total <= from + size)
						{
							// Ring buffer add
							list[(start + count) % size] = kvp.Value;

							// Grow until we reach capacity
							if (count < size)
							{
								count++;
							}
							// Otherwise - shift start position
							else
							{
								start = (start + 1) % size;
							}
						}
						// Otherwise - keep counting total number of packages
					}
				}
			}

			// https://github.com/npm/registry/blob/master/docs/REGISTRY-API.md#get-v1search
			using (var stream = new MemoryStream())
			{
				StreamWriter streamWriter = new StreamWriter(stream);
				JsonWriter writer = new JsonWriter(streamWriter);

				writer.WriteObjectStart();

				writer.WriteProperty("objects");
				writer.WriteArrayStart();
				for (int i = 0; i < count; i++)
				{
					PackageInfo package = list[(start + i) % count];
					var latest = package.versions[package.latest];

					writer.WriteObjectStart();  // object

					writer.WriteProperty("package");
					writer.WriteObjectStart();
					writer.Write("name", package.name);
					writer.Write("version", latest["version"]);
					writer.Write("description", latest.GetOrDefault("description", string.Empty));
					writer.Write("keywords", latest.GetOrDefault("keywords", null));
					// "date", "links", "publisher", "maintainers"
					writer.WriteObjectEnd();    // package

					// "score", "searchScore"

					writer.WriteObjectEnd();    // object
				}
				writer.WriteArrayEnd();     // objects

				writer.Write("total", total);
				//writer.Write("time", "Wed Jan 25 2017 19:23:35 GMT+0000 (UTC)");
				writer.WriteObjectEnd();    // root

				// 
				await streamWriter.FlushAsync();
				response.ContentType = mimeApplicationJson;
				response.ContentEncoding = Encoding.UTF8;
				response.ContentLength64 = stream.Length;       // Must be set explicitly before writing to response.OutputStream!
				stream.Seek(0L, SeekOrigin.Begin);
				await stream.CopyToAsync(response.OutputStream);
				response.Close();
			}
		}

		// Package File. fileName is expected to be unescaped
		static private async Task ResponsePackageFile(HttpListenerResponse response, string fileName, string httpMethod)
		{
			string fullPath = Path.Combine(pathPackages, fileName);
			FileInfo fileInfo = new FileInfo(fullPath);
			// Basic security - do not allow to serve anything outside of the packages folder
			if (fullPath.StartsWith(pathPackages, StringComparison.OrdinalIgnoreCase) && fileInfo.Exists)
			{
				// Unity also makes HEAD request for file
				if (httpMethod == "HEAD" || httpMethod == "GET")
				{
					response.ContentLength64 = fileInfo.Length;
					response.SendChunked = false;
					response.ContentType = System.Net.Mime.MediaTypeNames.Application.Octet;
					response.AddHeader("Content-Disposition", "attachment; filename=" + fileInfo.Name);
					// "Thu, 18 Mar 2021 18:03:58 GMT" https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Last-Modified
					response.AddHeader("Last-Modified", fileInfo.LastWriteTimeUtc.ToString("ddd, dd MMM yyyy HH:mm:ss GMT"));

					if (httpMethod == "GET")
					{
						using (FileStream fileStream = fileInfo.OpenRead())
						{
							// Write out to the response stream (asynchronously), then close it
							await fileStream.CopyToAsync(response.OutputStream);
						}
					}
					response.Close();
				}
				else
				{
					await ResponseError(response, "Unknown HttpMethod");
				}
			}
			else
			{
				// File was removed from the server when it was running
				await ResponseError(response, "File not found", HttpStatusCode.InternalServerError);
			}
		}
		
		// 
		static private async Task ResponseVersionMeta(HttpListenerResponse response, Dictionary<string, object> versionInfo, string urlPrefix)
		{
			using (var stream = new MemoryStream())
			{
				StreamWriter streamWriter = new StreamWriter(stream);
				JsonWriter writer = new JsonWriter(streamWriter);

				writer.WriteObjectStart();	// root
				
				foreach (var pair in versionInfo)
				{
					string key = pair.Key;
					if (keyComparer.Equals(key, "dist"))
					{
						var dist = pair.Value as Dictionary<string, object>;
						writer.WriteProperty("dist");
						writer.WriteObjectStart();

						writer.Write("shasum", dist["shasum"]);
						writer.Write("tarball", string.Format("{0}/{1}/-/{2}", urlPrefix, (string)versionInfo["name"], dist["tarball"]));

						writer.WriteObjectEnd();
					}
					else
					{
						writer.Write(pair.Key, pair.Value);
					}
				}

				writer.WriteObjectEnd();	// root

				await streamWriter.FlushAsync();
				response.ContentType = mimeApplicationJson;
				response.ContentEncoding = Encoding.UTF8;
				response.ContentLength64 = stream.Length;       // Must be set explicitly before writing to response.OutputStream!
				stream.Seek(0L, SeekOrigin.Begin);
				await stream.CopyToAsync(response.OutputStream);
				response.Close();
			}
		}

		// 
		static private async Task ResponsePackageMeta(HttpListenerResponse response, PackageInfo packageInfo, string urlPrefix)
		{
			// https://github.com/npm/registry/blob/master/docs/responses/package-metadata.md
			using (var stream = new MemoryStream())
			{
				// Don't explicitly specify Encoding.UTF8 since it will add BOM. By default - StreamWriter will use UTF8NoBOM encoding. 
				StreamWriter streamWriter = new StreamWriter(stream);
				JsonWriter writer = new JsonWriter(streamWriter);

				var latest = packageInfo.versions[packageInfo.latest];

				writer.WriteObjectStart();  // root

				writer.WriteProperty("dist-tags");
				writer.WriteObjectStart();
				writer.Write("latest", packageInfo.latest);
				writer.WriteObjectEnd();    // dist-tags

				writer.Write("name", packageInfo.name);
				writer.Write("description", latest["description"]);

				writer.WriteProperty("versions");
				writer.WriteObjectStart();
				foreach (var kvp in packageInfo.versions)
				{
					writer.WriteProperty(kvp.Key);  // version

					writer.WriteObjectStart();  // version info object
					var versionInfo = kvp.Value as Dictionary<string, object>;
					foreach (var property in versionInfo)
					{
						string propertyId = property.Key;
						if (keyComparer.Equals(propertyId, "dist"))
						{
							var dist = property.Value as Dictionary<string, object>;
							writer.WriteProperty(propertyId);
							writer.WriteObjectStart();
							writer.Write("shasum", dist["shasum"]);
							writer.Write("tarball", string.Format("{0}/{1}/-/{2}", urlPrefix, packageInfo.name, dist["tarball"]));
							writer.WriteObjectEnd();
						}
						else
						{
							writer.Write(propertyId, property.Value);
						}
					}
					writer.WriteObjectEnd();    // version info object
				}
				writer.WriteObjectEnd();    // versions

				writer.Write("time", packageInfo.time);
				object readme;
				if (latest.TryGetValue("readme", out readme))
				{
					writer.Write("readme", (string)readme);
				}
				writer.WriteObjectEnd();    // root

				await streamWriter.FlushAsync();
				response.ContentType = mimeApplicationJson;
				response.ContentEncoding = Encoding.UTF8;
				response.ContentLength64 = stream.Length;       // Must be set explicitly before writing to response.OutputStream!
				stream.Seek(0L, SeekOrigin.Begin);
				await stream.CopyToAsync(response.OutputStream);
				response.Close();
			}
		}

		// 
		static private async Task ResponseRoot(HttpListenerResponse response)
		{
			using (var stream = new MemoryStream())
			{
				// Don't explicitly specify Encoding.UTF8 since it will add BOM. By default - StreamWriter will use UTF8NoBOM encoding. 
				StreamWriter streamWriter = new StreamWriter(stream);
				JsonWriter writer = new JsonWriter(streamWriter);

				writer.WriteObjectStart();
				writer.Write("db_name", "registry");
				writer.WriteObjectEnd();

				await streamWriter.FlushAsync();
				response.ContentType = mimeApplicationJson;
				response.ContentEncoding = Encoding.UTF8;
				response.ContentLength64 = stream.Length;       // Must be set explicitly before writing to response.OutputStream!
				stream.Seek(0L, SeekOrigin.Begin);
				await stream.CopyToAsync(response.OutputStream);
				response.Close();
			}
		}

		// 
		static private async Task ResponseError(HttpListenerResponse response, string error, HttpStatusCode errorCode = HttpStatusCode.BadRequest)
		{
			using (var stream = new MemoryStream())
			{
				// Don't explicitly specify Encoding.UTF8 since it will add BOM. By default - StreamWriter will use UTF8NoBOM encoding. 
				StreamWriter streamWriter = new StreamWriter(stream);
				JsonWriter writer = new JsonWriter(streamWriter);

				writer.WriteObjectStart();
				writer.Write("statusCode", (int)errorCode);
				writer.Write("error", error);
				writer.WriteObjectEnd();

				await streamWriter.FlushAsync();
				response.StatusCode = (int)errorCode;
				response.ContentType = mimeApplicationJson;
				response.ContentEncoding = Encoding.UTF8;
				response.ContentLength64 = stream.Length;       // Must be set explicitly before writing to response.OutputStream!
				stream.Seek(0L, SeekOrigin.Begin);
				await stream.CopyToAsync(response.OutputStream);
				response.Close();
			}
		}

		// Console command input loop
		static private void ReadInput()
		{
			while (!terminated)
			{
				WritePrompt();

				string input = Console.ReadLine();
				hasPrompt = false;
				string[] args = input.Split(commandSeparators, StringSplitOptions.RemoveEmptyEntries);
				if (args.Length > 0)
				{
					Action<string[]> command;
					if (commands.TryGetValue(args[0], out command))
					{
						command(args);
					}
					else
					{
						Log("Unrecognized command '{0}'", args[0]);
					}
				}
			}
		}
		
		// 
		static private void WritePrompt()
		{
			if (hasPrompt) { return; }

			hasPrompt = true;
			Console.ForegroundColor = promptColor;
			if (Console.CursorLeft > 0) { Console.Write('\n'); }
			Console.Write(prompt);
			// TODO
			// Should also append existing input string here, but there is no way to read it since even Console.OpenStandardInput().Read() blocks until NewLine is entered. 
			// The workaround is to manually handle input via ReadKey() but that would require implementing InputField. 
		}
		#endregion

		#region Helpers
		// Build common url prefix
		static private string GetUrlPrefix(Uri url)
		{
			StringBuilder urlBuilder = new StringBuilder();
			urlBuilder.Append(url.Scheme).Append("://");
			if (!string.IsNullOrEmpty(url.UserInfo)) { urlBuilder.Append(url.UserInfo).Append('@'); }
			urlBuilder.Append(url.DnsSafeHost);
			if (!url.IsDefaultPort) { urlBuilder.Append(':').Append(url.Port); }
			return urlBuilder.ToString();
		}
		
		// 
		static private void Read(Stream stream, byte[] buffer, long size, Action<int> action)
		{
			if (size < 0) { throw new Exception("Size cannot be negative"); }

			long bytesRead = 0L;
			while (bytesRead < size)
			{
				int toRead = (int)Math.Min(buffer.Length, size - bytesRead);
				int length = stream.Read(buffer, 0, toRead);
				if (length < toRead)
				{
					throw new EndOfStreamException();
				}
				bytesRead += length;
				
				if (action != null)
				{
					action(length);
				}
			}
		}

		// Splits Uri.Query into Dictionary (?key1=value1&keyWithoutValue&key2=value2)
		static private void ParseQuery(string query, Dictionary<string, string> result)
		{
			result.Clear();

			string[] pairs = query.Split(new char[] { '?', '&' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < pairs.Length; i++)
			{
				string pair = pairs[i];
				int index = pair.IndexOf('=');

				string key = null;
				string value = null;
				if (index > 0)
				{
					key = Uri.UnescapeDataString(pair.Substring(0, index));
					value = Uri.UnescapeDataString(pair.Substring(index + 1));
				}
				else
				{
					key = pair;
				}
				result[key] = value;    // Add or overwrite (no duplicate keys allowed)
			}
		}

		// Debug helper to serialize any value into Json string
		static public string SerializeToJson(object value, bool prettyPrint = false)
		{
			using (var memoryStream = new MemoryStream())
			using (var streamWriter = new StreamWriter(memoryStream))
			{
				JsonWriter jsonWriter = new JsonWriter(streamWriter);
				jsonWriter.prettyPrint = prettyPrint;
				jsonWriter.WriteValue(value);
				streamWriter.Flush();
				return Encoding.UTF8.GetString(memoryStream.ToArray());
			}
		}

		// 
		static private void Apply(TarHeader header, Dictionary<string, string> records)
		{
			string value;

			if (records.TryGetValue("path", out value)) { header.name = value; }
			if (records.TryGetValue("linkpath", out value)) { header.linkname = value; }
			if (records.TryGetValue("uname", out value)) { header.uname = value; }
			if (records.TryGetValue("gname", out value)) { header.gname = value; }
			if (records.TryGetValue("mtime", out value))
			{
				double seconds;
				if (double.TryParse(value, out seconds))
				{
					// Decimal representation of the time in seconds since the Epoch. If a <period> ( '.' ) decimal point character is present, 
					// the digits to the right of the point shall represent the units of a subsecond timing granularity, where the first digit 
					// is tenths of a second and each subsequent digit is a tenth of the previous digit
					header.mtime = new DateTime(Epoch.Ticks + (long)(seconds * TicksPerSecond));
				}
				else
				{
					Log(LogType.Error, "Cannot parse mtime value '{0}' as double.", value);
				}
			}
			if (records.TryGetValue("uid", out value))
			{
				int uid;
				if (int.TryParse(value, out uid))
				{
					header.uid = uid;
				}
				else
				{
					Log(LogType.Error, "Cannot parse uid value '{0}' as int.", value);
				}
			}
			if (records.TryGetValue("gid", out value))
			{
				int gid;
				if (int.TryParse(value, out gid))
				{
					header.gid = gid;
				}
				else
				{
					Log(LogType.Error, "Cannot parse gid value '{0}' as int.", value);
				}
			}
			if (records.TryGetValue("size", out value))
			{
				long size;
				if (long.TryParse(value, out size))
				{
					header.size = size;
				}
				else
				{
					Log(LogType.Error, "Cannot parse size value '{0}' as long.", value);
				}
			}
			
			// TODO atime charset comment hdrcharset
		}
		#endregion

		#region Logging
		// Main
		static private void LogImpl(ConsoleColor color, string message)
		{
			ConsoleColor cached = Console.ForegroundColor;
			Console.ForegroundColor = color;

			bool hadPrompt = hasPrompt;
			hasPrompt = false;
			Console.CursorLeft = 0;
			Console.Write(message);
			Console.Write('\n');
			Console.ForegroundColor = cached;

			if (hadPrompt)
			{
				WritePrompt();
			}
		}

		// 
		static private ConsoleColor GetColor(LogType logType)
		{
			switch (logType)
			{
				case LogType.Exception:
				case LogType.Error:
					return ConsoleColor.Red;

				case LogType.Warning:
					return ConsoleColor.Yellow;

				case LogType.Log:
				case LogType.Info:
					return defaultColor;

				default:
					return Console.ForegroundColor;
			}
		}
		
		// 
		static public bool IsVerbose(LogType value)
		{
			return verbosity >= value;
		}

		// 
		static public void Log(ConsoleColor color, string message)
		{
			LogImpl(color, message);
		}

		// 
		static public void Log(string message)
		{
			LogImpl(defaultColor, message);
		}
		
		// 
		static public void Log(Exception ex)
		{
			Log(LogType.Exception, ex.ToString());
		}

		//
		static public void Log(object obj)
		{
			LogImpl(defaultColor, obj != null ? obj.ToString() : "null");
		}

		// 
		static public void Log(ConsoleColor color, string format, params object[] args)
		{
			LogImpl(color, string.Format(format, args));
		}

		// 
		static public void Log(string format, params object[] args)
		{
			LogImpl(defaultColor, string.Format(format, args));
		}

		// 
		static public void Log(LogType logType, string message)
		{
			if (verbosity < logType) { return; }

			LogImpl(GetColor(logType), message);
		}

		// 
		static public void Log(LogType logType, string format, params object[] args)
		{
			if (verbosity < logType) { return; }

			LogImpl(GetColor(logType), string.Format(format, args));
		}
		#endregion

		#region Commands
		//
		static private void RegisterCommands()
		{
			commands = new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase);

			commands.Add("help", CommandHelp);
			commands.Add("clear", CommandClear);
			commands.Add("start", CommandStart);
			commands.Add("stop", CommandStop);
			commands.Add("restart", CommandRestart);
			commands.Add("list", CommandList);
			commands.Add("scan", CommandScan);
			commands.Add("shutdown", CommandShutdown);
			commands.Add("quit", CommandShutdown);
			commands.Add("exit", CommandShutdown);
			commands.Add("verbosity", CommandVerbosity);
		}

		// 
		static private void CommandHelp(string[] args)
		{
			StringBuilder sb = new StringBuilder("Available commands:");
			foreach (var kvp in commands)
			{
				if (sb.Length > 0) { sb.Append(' '); }
				sb.Append(kvp.Key);
			}
			Log(sb);
		}

		// 
		static private void CommandShutdown(string[] args)
		{
			StopServer();
			listener.Close();
			terminated = true;
		}
		
		// 
		static private void CommandClear(string[] args)
		{
			Console.Clear();
		}

		// 
		static private void CommandList(string[] args)
		{
			if (registry.Count == 0)
			{
				Log("0 packages found in '{0}'", pathPackages);
				return;
			}

			List<string> list = new List<string>();
			using (var e = registry.GetEnumerator())
			{
				while (e.MoveNext())
				{
					var kvp = e.Current;
					string id = kvp.Key;
					list.Add(id);
				}
			}
			list.Sort();
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < list.Count; i++)
			{
				sb.Length = 0;
				string name = list[i];
				PackageInfo packageInfo = registry[name];
				sb.AppendFormat("{0} (", name);

				bool first = true;
				foreach (var kvp in packageInfo.versions)
				{
					if (first) { first = false; }
					else { sb.Append(", "); }
					sb.Append(kvp.Key);
				}
				sb.Append(")");
				Log(sb.ToString());
			}
		}
		
		// 
		static private void CommandStart(string[] args)
		{
			StartServer();
		}
		
		// 
		static private void CommandStop(string[] args)
		{
			StopServer();
		}
		
		// 
		static private void CommandRestart(string[] args)
		{
			if (listener.IsListening) { StopServer(); }
			StartServer();
		}

		// 
		static private void CommandScan(string[] args)
		{
			bool restart = listener.IsListening;
			if (restart) { StopServer(); }
			ParsePackages();
			if (restart) { StartServer(); }
		}

		// 
		static private void CommandVerbosity(string[] args)
		{
			string arg1 = args != null && args.Length > 1 ? args[1] : null;
			if (!string.IsNullOrEmpty(arg1))
			{
				LogType logType;
				if (Enum.TryParse(arg1, true, out logType))
				{
					verbosity = logType;
					Log("Verbosity now: {0}", verbosity);
				}
				// Bad value typed - show usage
				else
				{
					Log("Unable to parse '{0}' as LogType. Possible values: {1}", arg1, string.Join(", ", Enum.GetNames(typeof(LogType))));
				}
			}
			// No value typed - show current value
			else
			{
				Log("Verbosity: {0}", verbosity);
			}
		}
		#endregion
	}
}