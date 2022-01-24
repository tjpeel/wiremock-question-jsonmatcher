using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using WireMock.Handlers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace wiremock
{
    internal static class Program
    {
        private const int PortStartNumber = 5001;

        private static void Main(string[] args)
        {
            /*
             * This test project shows a potential issue related to recording requests and then
             * using the recorded mapping JSON files in order to provide a stub server that
             * can return the same response based on the recorded request.
             *
             * The issue might be a misconfiguration or be by design, which is why this example
             * has been made.
             *
             * Servers:
             *
             * 1. A wiremock instance representing a downstream service
             * 2. A wiremock instance that proxies to the downstream service and records mappings
             * 3. A wiremock instance that uses a pre-recorded mapping (jsonpatterns)
             *          The request body JsonMatcher is as was recorded via the proxying server (2) above
             *          The Patterns property of the JsonMatcher is an array, which then contains the request body
             *              that was recorded
             *          There have been no manual changes to this file, it's what was recorded
             *          When the same request is processed by this server, it cannot match as it compares an array
             *              of request boyd values to a single request body; so this results in a 404 
             * 4. A wiremock instances that uses an adapted pre-recorded mapping (jsonpattern)
             *          The request body JsonMatcher was changed manually from Patterns -> Pattern
             *          The Pattern property is the request payload and not an array
             *          When the same request is processed by this server, it can match and return the response
             */
            
            ClearPreviouslySavedMappingRecordingFilesFromBin();

            var downstreamServiceServer = StartDownstreamServiceServer();
            var proxyToDownstreamServiceServer = StartProxyingServer(downstreamServiceServer.Urls.First());
            var jsonPatternsServer = RecordedWithJsonPatternsServer();
            var jsonPatternServer = RecordedWithJsonPatternServer();

            Console.WriteLine("Call proxy service, which should succeed:");
            var proxyResponse = PostRequestWithExpectedBody(proxyToDownstreamServiceServer);
            Console.WriteLine(proxyResponse.StatusCode);
            Console.WriteLine(proxyResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Console.WriteLine();

            Console.WriteLine("Call recorded server with request body JsonMatcher with " +
                              "Patterns property (which was saved via the proxying server)");
            Console.WriteLine("Response should be not found");
            var jsonPatternsResponse = PostRequestWithExpectedBody(jsonPatternsServer);
            Console.WriteLine(jsonPatternsResponse.StatusCode);
            Console.WriteLine(jsonPatternsResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());

            Console.WriteLine("Call recorded server with request body JsonMatcher with " +
                              "Pattern property");
            Console.WriteLine("Response should now work");
            var jsonPatternResponse = PostRequestWithExpectedBody(jsonPatternServer);
            Console.WriteLine(jsonPatternResponse.StatusCode);
            Console.WriteLine(jsonPatternResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());

            Console.ReadLine();

            downstreamServiceServer.Dispose();
            proxyToDownstreamServiceServer.Dispose();
            jsonPatternsServer.Dispose();
            jsonPatternServer.Dispose();
        }

        private static HttpResponseMessage PostRequestWithExpectedBody(WireMockServer server)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(server.Urls.First())
            };

            var response = httpClient.PostAsJsonAsync("/endpoint", new
            {
                post1 = "value 1",
                post2 = "value 2"
            }).GetAwaiter().GetResult();

            return response;
        }

        private static WireMockServer RecordedWithJsonPatternsServer()
        {
            var server = WireMockServer.Start(new WireMockServerSettings
            {
                Port = PortStartNumber + 2,
                ReadStaticMappings = true,
                FileSystemHandler = new EmbeddedResourceFileSystemHandler("wiremock.instances.jsonpatterns")
            });

            return server;
        }

        private static WireMockServer RecordedWithJsonPatternServer()
        {
            var server = WireMockServer.Start(new WireMockServerSettings
            {
                Port = PortStartNumber + 3,
                ReadStaticMappings = true,
                FileSystemHandler = new EmbeddedResourceFileSystemHandler("wiremock.instances.jsonpattern")
            });

            return server;
        }

        private static WireMockServer StartProxyingServer(string url)
        {
            var server = WireMockServer.Start(new WireMockServerSettings
            {
                Port = PortStartNumber + 1,
                ProxyAndRecordSettings = new ProxyAndRecordSettings
                {
                    Url = url,
                    SaveMapping = true,
                    SaveMappingToFile = true,
                    SaveMappingForStatusCodePattern = "2xx",
                    AllowAutoRedirect = true,
                    ExcludedHeaders = new[]
                    {
                        "Host", "Authorization"
                    }
                }
            });

            return server;
        }

        private static WireMockServer StartDownstreamServiceServer()
        {
            var server = WireMockServer.Start(PortStartNumber);

            server
                .Given(Request.Create()
                    .UsingPost()
                    .WithPath("/endpoint"))
                .RespondWith(Response.Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithBodyAsJson(new
                    {
                        data1 = "the data1",
                        data2 = new
                        {
                            id = "the id",
                            mame = "the name"
                        }
                    }));

            return server;
        }

        private static void ClearPreviouslySavedMappingRecordingFilesFromBin()
        {
            var mappingsDirectory =
                new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "__admin", "mappings"));

            if (mappingsDirectory.Exists)
                mappingsDirectory.Delete(true);
        }
    }

    internal class EmbeddedResourceFileSystemHandler : IFileSystemHandler
    {
        private readonly string _rootNamespace;

        private const string AdminMappingsFolder = "__admin.mappings";

        public EmbeddedResourceFileSystemHandler(string rootNamespace)
        {
            _rootNamespace = rootNamespace;
        }

        public string GetMappingFolder() => string.Join(".", _rootNamespace, AdminMappingsFolder);

        public bool FolderExists(string path) => Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .FirstOrDefault(x => x.StartsWith(path)) != null;

        public void CreateFolder(string path) => throw new NotImplementedException();

        public IEnumerable<string> EnumerateFiles(string path, bool includeSubdirectories) =>
            Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .Where(x => x.StartsWith(path) && x.EndsWith(".json"));

        public string ReadMappingFile(string path) => Read(path);

        public void WriteMappingFile(string path, string text) => throw new NotImplementedException();

        public byte[] ReadResponseBodyAsFile(string path) => throw new NotImplementedException();

        public string ReadResponseBodyAsString(string path) => throw new NotImplementedException();

        public void DeleteFile(string filename) => throw new NotImplementedException();

        public bool FileExists(string filename) => throw new NotImplementedException();

        public void WriteFile(string filename, byte[] bytes) => throw new NotImplementedException();

        public byte[] ReadFile(string filename) => throw new NotImplementedException();

        public string ReadFileAsString(string filename) => throw new NotImplementedException();

        public string GetUnmatchedRequestsFolder() => throw new NotImplementedException();

        public void WriteUnmatchedRequest(string filename, string text) => throw new NotImplementedException();

        private static string Read(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) throw new Exception($"{name} embedded resource could not be found");

            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }
}