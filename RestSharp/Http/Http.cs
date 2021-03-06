﻿#region License
//   Copyright 2010 John Sheehan
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License. 
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;

using RestSharp.Extensions;

namespace RestSharp
{
	public class Http : IHttp
	{
		protected bool HasParameters {
			get {
				return Parameters.Any();
			}
		}

		protected bool HasBody {
			get {
				return !string.IsNullOrEmpty(RequestBody);
			}
		}

		protected bool HasFiles {
			get {
				return Files.Any();
			}
		}

		public ICredentials Credentials { get; set; }
		public IList<HttpFile> Files { get; private set; }
		public IList<HttpHeader> Headers { get; private set; }
		public IList<HttpParameter> Parameters { get; private set; }
		public IWebProxy Proxy { get; set; }
		public string RequestBody { get; set; }

		public Uri Url { get; set; }

		public Http() {
			Headers = new List<HttpHeader>();
			Files = new List<HttpFile>();
			Parameters = new List<HttpParameter>();
		}

		public RestResponse Post() {
			return PostPutInternal("POST");
		}

		public RestResponse Put() {
			return PostPutInternal("PUT");
		}

		private RestResponse PostPutInternal(string method) {

			var webRequest = (HttpWebRequest)WebRequest.Create(Url);
			webRequest.Method = method;

			if (Credentials != null) {
				webRequest.Credentials = Credentials;
			}

			if (Proxy != null) {
				webRequest.Proxy = Proxy;
			}

			if (HasFiles) {
				webRequest.ContentType = GetMultipartFormContentType();
				WriteMultipartFormData(webRequest);
			}
			else {
				if (HasParameters) {
					webRequest.ContentType = "application/x-www-form-urlencoded";
					RequestBody = EncodeParameters();
				}
				else if (HasBody) {
					webRequest.ContentType = "text/xml";
				}
			}

			WriteRequestBody(webRequest);
			AppendHeaders(webRequest);
			return GetResponse(webRequest);
		}

		private void WriteRequestBody(HttpWebRequest webRequest) {
			if (HasBody) {
				webRequest.ContentLength = RequestBody.Length;

				var requestStream = webRequest.GetRequestStream();
				using (StreamWriter writer = new StreamWriter(requestStream, Encoding.ASCII)) {
					writer.Write(RequestBody);
				}
			}
		}

		private string _formBoundary = "-----------------------------28947758029299";
		private string GetMultipartFormContentType() {
			return string.Format("multipart/form-data; boundary={0}", _formBoundary);
		}

		private void WriteMultipartFormData(HttpWebRequest webRequest) {
			var boundary = _formBoundary;
			var encoding = Encoding.ASCII;
			using (Stream formDataStream = webRequest.GetRequestStream()) {
				foreach (var file in Files) {
					var fileName = file.FileName;
					var data = file.Data;
					var length = data.Length;
					var contentType = file.ContentType;
					// Add just the first part of this param, since we will write the file data directly to the Stream
					string header = string.Format("--{0}{3}Content-Disposition: form-data; name=\"{1}\"; filename=\"{1}\";{3}Content-Type: {2}{3}{3}",
													boundary,
													fileName,
													contentType ?? "application/octet-stream",
													Environment.NewLine);

					formDataStream.Write(encoding.GetBytes(header), 0, header.Length);
					// Write the file data directly to the Stream, rather than serializing it to a string.
					formDataStream.Write(data, 0, length);
					string lineEnding = Environment.NewLine;
					formDataStream.Write(encoding.GetBytes(lineEnding), 0, lineEnding.Length);
				}

				foreach (var param in Parameters) {
					var postData = string.Format("--{0}{3}Content-Disposition: form-data; name=\"{1}\"{3}{3}{2}{3}",
													boundary,
													param.Name,
													param.Value,
													Environment.NewLine);

					formDataStream.Write(encoding.GetBytes(postData), 0, postData.Length);
				}

				string footer = String.Format("{1}--{0}--{1}", boundary, Environment.NewLine);
				formDataStream.Write(encoding.GetBytes(footer), 0, footer.Length);
			}
		}

		private string EncodeParameters() {
			var querystring = new StringBuilder();
			foreach (var p in Parameters) {
				if (querystring.Length > 1)
					querystring.Append("&");
				querystring.AppendFormat("{0}={1}", HttpUtility.UrlEncode(p.Name), HttpUtility.UrlEncode(p.Value));
			}

			return querystring.ToString();
		}

		public RestResponse Get() {
			return GetStyleVerbInternal("GET");
		}

		public RestResponse Head() {
			return GetStyleVerbInternal("HEAD");
		}

		public RestResponse Options() {
			return GetStyleVerbInternal("OPTIONS");
		}

		public RestResponse Delete() {
			return GetStyleVerbInternal("DELETE");
		}

		private RestResponse GetStyleVerbInternal(string method) {
			string url = Url.ToString();
			if (HasParameters) {
				var data = EncodeParameters();
				url = string.Format("{0}?{1}", url, data);
			}

			var webRequest = (HttpWebRequest)WebRequest.Create(url);
			webRequest.Method = method;

			if (this.Credentials != null) {
				webRequest.Credentials = this.Credentials;
			}

			if (this.Proxy != null) {
				webRequest.Proxy = this.Proxy;
			}

			// incompatible with GET, not sure about DELETE, OPTIONS, HEAD
			//if (HasBody) {
			//    webRequest.ContentType = "text/xml";
			//}

			//WriteRequestBody(webRequest);
			AppendHeaders(webRequest);
			return GetResponse(webRequest);
		}

		// handle restricted headers the .NET way - thanks @dimebrain!
		// http://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.headers.aspx
		private void AppendHeaders(HttpWebRequest webRequest) {
			foreach (var header in Headers) {
				if (_restrictedHeaderActions.ContainsKey(header.Name)) {
					_restrictedHeaderActions[header.Name].Invoke(webRequest, header.Value);
				}
				else {
					webRequest.Headers[header.Name] = header.Value;
				}
			}
		}

		private readonly IDictionary<string, Action<HttpWebRequest, string>> _restrictedHeaderActions
			= new Dictionary<string, Action<HttpWebRequest, string>>(StringComparer.OrdinalIgnoreCase) {
                      { "Accept",            (r, v) => r.Accept = v },
                      { "Connection",        (r, v) => r.Connection = v },           
                      { "Content-Length",    (r, v) => r.ContentLength = Convert.ToInt64(v) },
                      { "Content-Type",      (r, v) => r.ContentType = v },
                      { "Expect",            (r, v) => r.Expect = v },
                      { "Date",              (r, v) => { /* Set by system */ }},
                      { "Host",              (r, v) => { /* Set by system */ }},
                      { "If-Modified-Since", (r, v) => r.IfModifiedSince = Convert.ToDateTime(v) },
                      { "Range",             (r, v) => { throw new NotImplementedException(/* r.AddRange() */); }},
                      { "Referer",           (r, v) => r.Referer = v },
                      { "Transfer-Encoding", (r, v) => { r.TransferEncoding = v; r.SendChunked = true; } },
                      { "User-Agent",        (r, v) => r.UserAgent = v }             
                  };

		private RestResponse GetResponse(HttpWebRequest request) {
			var response = new RestResponse();
			response.ResponseStatus = ResponseStatus.None;

			try {
				var webResponse = GetRawResponse(request);
				using (webResponse) {
					response.ContentType = webResponse.ContentType;
					response.ContentLength = webResponse.ContentLength;
					response.ContentEncoding = webResponse.ContentEncoding;
					response.Content = webResponse.GetResponseStream().ReadAsString();
					response.StatusCode = webResponse.StatusCode;
					response.StatusDescription = webResponse.StatusDescription;
					response.ResponseUri = webResponse.ResponseUri;
					response.Server = webResponse.Server;
					response.ResponseStatus = ResponseStatus.Success;

					webResponse.Close();
				}
			}
			catch (Exception ex) {
				response.ErrorMessage = ex.Message;
				response.ResponseStatus = ResponseStatus.Error;
			}

			return response;
		}

		private HttpWebResponse GetRawResponse(HttpWebRequest request) {
			HttpWebResponse raw = null;
			try {
				raw = (HttpWebResponse)request.GetResponse();
			}
			catch (WebException ex) {
				if (ex.Response is HttpWebResponse) {
					raw = ex.Response as HttpWebResponse;
				}
			}

			return raw;
		}
	}
}
