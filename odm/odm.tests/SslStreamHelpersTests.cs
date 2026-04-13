using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using odm.core;

namespace odm.tests
{
    /// <summary>
    /// Unit tests for the internal SslStreamHelpers module functions:
    /// decodeChunked, parseResponse, and stripDoctype.
    /// Accessible via InternalsVisibleTo declared in onvif.session AssemblyInfo.fs.
    /// </summary>
    [TestClass]
    public class SslStreamHelpersTests
    {
        // ── decodeChunked ──────────────────────────────────────────────────────────

        [TestMethod]
        public void DecodeChunked_SingleChunk_ReturnsChunkData()
        {
            var input = Encoding.ASCII.GetBytes("5\r\nHello\r\n0\r\n\r\n");
            var result = SslStreamHelpers.decodeChunked(input);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("Hello"), result);
        }

        [TestMethod]
        public void DecodeChunked_TwoChunks_ReturnsConcatenated()
        {
            var input = Encoding.ASCII.GetBytes("5\r\nHello\r\n6\r\nWorld!\r\n0\r\n\r\n");
            var result = SslStreamHelpers.decodeChunked(input);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("HelloWorld!"), result);
        }

        [TestMethod]
        public void DecodeChunked_TerminatorOnly_ReturnsEmpty()
        {
            var input = Encoding.ASCII.GetBytes("0\r\n\r\n");
            var result = SslStreamHelpers.decodeChunked(input);
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

            var resp = SslStreamHelpers.parseResponse(raw);

            Assert.AreEqual(200, resp.StatusCode);
            CollectionAssert.AreEqual(bodyBytes, resp.Body);
            StringAssert.Contains(resp.ContentType, "application/soap+xml");
        }

        [TestMethod]
        public void ParseResponse_ChunkedTransferEncoding_BodyDecodedCorrectly()
        {
            var chunk = "<ok/>";
            var chunkSize = Encoding.UTF8.GetByteCount(chunk);
            var chunkedBody = Encoding.ASCII.GetBytes(
                string.Format("{0:x}\r\n{1}\r\n0\r\n\r\n", chunkSize, chunk));

            var raw = BuildResponse(200,
                "Transfer-Encoding: chunked\r\n" +
                "Content-Type: application/soap+xml\r\n",
                chunkedBody);

            var resp = SslStreamHelpers.parseResponse(raw);

            Assert.AreEqual(200, resp.StatusCode);
            var decoded = Encoding.UTF8.GetString(resp.Body);
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

            var resp = SslStreamHelpers.parseResponse(raw);

            Assert.AreEqual(401, resp.StatusCode);
            Assert.IsTrue(resp.Body.Length > 0, "Body should be returned even for 401");
        }

        // ── stripDoctype ───────────────────────────────────────────────────────────

        [TestMethod]
        public void StripDoctype_InputWithDoctype_DoctypeRemoved()
        {
            var input = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\"?><!DOCTYPE foo><root/>");
            var result = SslStreamHelpers.stripDoctype(input);
            var text = Encoding.UTF8.GetString(result);
            Assert.IsFalse(text.Contains("<!DOCTYPE"), "DOCTYPE should be removed");
            StringAssert.Contains(text, "<root/>");
        }

        [TestMethod]
        public void StripDoctype_InputWithoutDoctype_SameBytesReturned()
        {
            var input = Encoding.UTF8.GetBytes("<root><child/></root>");
            var result = SslStreamHelpers.stripDoctype(input);
            Assert.AreSame(input, result, "Original array should be returned when no DOCTYPE is present");
        }

        [TestMethod]
        public void StripDoctype_DoctypeWithInternalSubset_AlsoStripped()
        {
            var input = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ELEMENT foo ANY>]><root/>");
            var result = SslStreamHelpers.stripDoctype(input);
            var text = Encoding.UTF8.GetString(result);
            Assert.IsFalse(text.Contains("<!DOCTYPE"), "DOCTYPE with internal subset should be removed");
            StringAssert.Contains(text, "<root/>");
        }
    }
}
