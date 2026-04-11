using System;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// Unit tests for the internal SslStreamHelpers module functions:
    /// decodeChunked, parseResponse, and stripDoctype.
    /// These are accessed via reflection because the module is internal to onvif.session.
    /// </summary>
    [TestClass]
    public class SslStreamHelpersTests
    {
        private static Type _helpersType;
        private static MethodInfo _decodeChunked;
        private static MethodInfo _parseResponse;
        private static MethodInfo _stripDoctype;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            var assembly = typeof(SslStreamTransportBindingElement).Assembly;
            _helpersType = assembly.GetType("odm.core.SslStreamHelpers");
            Assert.IsNotNull(_helpersType, "SslStreamHelpers type not found in onvif.session assembly");

            _decodeChunked = _helpersType.GetMethod("decodeChunked",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _parseResponse = _helpersType.GetMethod("parseResponse",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _stripDoctype = _helpersType.GetMethod("stripDoctype",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.IsNotNull(_decodeChunked, "decodeChunked method not found");
            Assert.IsNotNull(_parseResponse, "parseResponse method not found");
            Assert.IsNotNull(_stripDoctype, "stripDoctype method not found");
        }

        private static byte[] DecodeChunked(byte[] data)
            => (byte[])_decodeChunked.Invoke(null, new object[] { data });

        private static byte[] StripDoctype(byte[] data)
            => (byte[])_stripDoctype.Invoke(null, new object[] { data });

        private static object ParseResponse(byte[] data)
            => _parseResponse.Invoke(null, new object[] { data });

        // HttpResponse is nested inside the internal SslStreamHelpers module, so its
        // properties are only visible via NonPublic binding flags in reflection.
        private static readonly BindingFlags RecordProps =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static int GetStatusCode(object resp)
            => (int)resp.GetType().GetProperty("StatusCode", RecordProps).GetValue(resp, null);

        private static byte[] GetBody(object resp)
            => (byte[])resp.GetType().GetProperty("Body", RecordProps).GetValue(resp, null);

        private static string GetContentType(object resp)
            => (string)resp.GetType().GetProperty("ContentType", RecordProps).GetValue(resp, null);

        // ── decodeChunked ──────────────────────────────────────────────────────────

        [TestMethod]
        public void DecodeChunked_SingleChunk_ReturnsChunkData()
        {
            var input = Encoding.ASCII.GetBytes("5\r\nHello\r\n0\r\n\r\n");
            var result = DecodeChunked(input);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("Hello"), result);
        }

        [TestMethod]
        public void DecodeChunked_TwoChunks_ReturnsConcatenated()
        {
            var input = Encoding.ASCII.GetBytes("5\r\nHello\r\n6\r\nWorld!\r\n0\r\n\r\n");
            var result = DecodeChunked(input);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("HelloWorld!"), result);
        }

        [TestMethod]
        public void DecodeChunked_TerminatorOnly_ReturnsEmpty()
        {
            var input = Encoding.ASCII.GetBytes("0\r\n\r\n");
            var result = DecodeChunked(input);
            Assert.AreEqual(0, result.Length);
        }

        // ── parseResponse ──────────────────────────────────────────────────────────

        private static byte[] BuildResponse(int status, string headers, byte[] body)
        {
            var statusLine = string.Format("HTTP/1.1 {0} {1}\r\n", status,
                status == 200 ? "OK" : status == 401 ? "Unauthorized" : "Error");
            var head = Encoding.ASCII.GetBytes(statusLine + headers + "\r\n");
            var full = new byte[head.Length + body.Length];
            Buffer.BlockCopy(head, 0, full, 0, head.Length);
            Buffer.BlockCopy(body, 0, full, head.Length, body.Length);
            return full;
        }

        [TestMethod]
        public void ParseResponse_ContentLengthBody_CorrectStatusCodeBodyAndContentType()
        {
            var bodyBytes = Encoding.UTF8.GetBytes("<soap>hello</soap>");
            var raw = BuildResponse(200,
                "Content-Type: application/soap+xml; charset=utf-8\r\n" +
                "Content-Length: " + bodyBytes.Length + "\r\n",
                bodyBytes);

            var resp = ParseResponse(raw);

            Assert.AreEqual(200, GetStatusCode(resp));
            CollectionAssert.AreEqual(bodyBytes, GetBody(resp));
            StringAssert.Contains(GetContentType(resp), "application/soap+xml");
        }

        [TestMethod]
        public void ParseResponse_ChunkedTransferEncoding_BodyDecodedCorrectly()
        {
            // chunked body: one chunk carrying "<ok/>"
            var chunk = "<ok/>";
            var chunkSize = Encoding.UTF8.GetByteCount(chunk);
            var chunkedBody = Encoding.ASCII.GetBytes(
                string.Format("{0:x}\r\n{1}\r\n0\r\n\r\n", chunkSize, chunk));

            var raw = BuildResponse(200,
                "Transfer-Encoding: chunked\r\n" +
                "Content-Type: application/soap+xml\r\n",
                chunkedBody);

            var resp = ParseResponse(raw);

            Assert.AreEqual(200, GetStatusCode(resp));
            var decoded = Encoding.UTF8.GetString(GetBody(resp));
            Assert.AreEqual(chunk, decoded);
        }

        [TestMethod]
        public void ParseResponse_401Response_StatusCodeIs401AndBodyReturned()
        {
            var bodyBytes = Encoding.UTF8.GetBytes("<html>Unauthorized</html>");
            var raw = BuildResponse(401,
                "Content-Type: text/html\r\n" +
                "Content-Length: " + bodyBytes.Length + "\r\n",
                bodyBytes);

            var resp = ParseResponse(raw);

            Assert.AreEqual(401, GetStatusCode(resp));
            Assert.IsTrue(GetBody(resp).Length > 0, "Body should be returned even for 401");
        }

        // ── stripDoctype ───────────────────────────────────────────────────────────

        [TestMethod]
        public void StripDoctype_InputWithDoctype_DoctypeRemoved()
        {
            var input = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\"?><!DOCTYPE foo><root/>");
            var result = StripDoctype(input);
            var text = Encoding.UTF8.GetString(result);
            Assert.IsFalse(text.Contains("<!DOCTYPE"), "DOCTYPE should be removed");
            StringAssert.Contains(text, "<root/>");
        }

        [TestMethod]
        public void StripDoctype_InputWithoutDoctype_SameBytesReturned()
        {
            var input = Encoding.UTF8.GetBytes("<root><child/></root>");
            var result = StripDoctype(input);
            // No DOCTYPE: original byte array reference is returned unchanged.
            Assert.AreSame(input, result, "Original array should be returned when no DOCTYPE is present");
        }

        [TestMethod]
        public void StripDoctype_DoctypeWithInternalSubset_AlsoStripped()
        {
            var input = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ELEMENT foo ANY>]><root/>");
            var result = StripDoctype(input);
            var text = Encoding.UTF8.GetString(result);
            Assert.IsFalse(text.Contains("<!DOCTYPE"), "DOCTYPE with internal subset should be removed");
            StringAssert.Contains(text, "<root/>");
        }
    }
}
