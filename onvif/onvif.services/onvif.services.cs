namespace onvif.services {
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Diagnostics;
	using System.Globalization;
	using System.Linq;
	using System.Runtime.Serialization;
	using System.ServiceModel;
	using System.Text;
	using System.Xml;
	using System.Xml.Schema;
	using System.Xml.Serialization;
	using System.Xml.Linq;
	using System.Reflection;
	using System.Dynamic;
	using utils;

	public interface IBaseVideoEncoderOptions {

		/// <summary>
		/// List of supported image sizes.
		/// </summary>
		VideoResolution[] resolutionsAvailable { get; set; }
	
		/// <summary>
		/// Supported frame rate in fps (frames per second).
		/// </summary>
		IntRange frameRateRange { get; set; }

		/// <summary>
		/// Supported encoding interval range. The encoding interval corresponds to the number of frames devided by the encoded frames. An encoding interval value of "1" means that all frames are encoded.
		/// </summary>
		IntRange encodingIntervalRange { get; set; }
	}
	public partial class JpegOptions : IBaseVideoEncoderOptions {
	}
	public partial class Mpeg4Options : IBaseVideoEncoderOptions {
	}
	public partial class H264Options : IBaseVideoEncoderOptions {
	}
	public partial class JpegOptions2 : IBaseVideoEncoderOptions {
	}
	public partial class Mpeg4Options2 : IBaseVideoEncoderOptions {
	}
	public partial class H264Options2 : IBaseVideoEncoderOptions {
	}
	public partial class H265Options : IBaseVideoEncoderOptions {
	}
	public partial class H265Options2 : IBaseVideoEncoderOptions {
	}


	public interface IBaseVideoDecoderOptions {
		/// <summary>
		/// List of supported Video Resolutions
		/// </summary>
		VideoResolution[] resolutionsAvailable { get; set; }

		/// <summary>
		/// Supported bitrate range in kbps
		/// </summary>
		IntRange supportedInputBitrate { get; set; }

		/// <summary>
		/// Supported framerate range in fps
		/// </summary>
		IntRange supportedFrameRate { get; set; }
	}
	public partial class JpegDecOptions : IBaseVideoDecoderOptions {
	}
	public partial class H264DecOptions : IBaseVideoDecoderOptions {
	}
	public partial class Mpeg4DecOptions : IBaseVideoDecoderOptions {
	}

	public interface IBaseAudioDecoderOptions {
		/// <summary>
		/// List of supported bitrates in kbps
		/// </summary>
		IntList bitrate { get; set; }

		/// <summary>
		/// List of supported sample rates in kHz
		/// </summary>
		IntList sampleRateRange { get; set; }
	}
	public partial class AACDecOptions : IBaseAudioDecoderOptions {
	}
	public partial class G711DecOptions : IBaseAudioDecoderOptions {
	}
	public partial class G726DecOptions : IBaseAudioDecoderOptions {
	}

	public interface IDeviceEntity {
		string token { get; set; }
	}
	public partial class VideoOutput : IDeviceEntity {
	}
	public partial class AudioOutput : IDeviceEntity {
	}
	public partial class NetworkInterface : IDeviceEntity {
	}
	public partial class RelayOutput : IDeviceEntity {
	}
	public partial class PTZNode : IDeviceEntity {
	}

	public interface IConfigurationEntity {
		string token { get; set; }
		string name { get; set; }
		int useCount { get; set; }
	}
	public static class ConfigurationEntityExtensions {
		public static string GetName(this IConfigurationEntity cgfEntity) {
			var name = cgfEntity.name;
			if (!String.IsNullOrWhiteSpace(name)) {
				return name;
			}
			return cgfEntity.token;
		}
	}

	public partial class ConfigurationEntity : IConfigurationEntity {
	}
	public partial class VideoSourceConfiguration : IConfigurationEntity {
	}
	public partial class VideoEncoderConfiguration : IConfigurationEntity {
	}
	public partial class AudioSourceConfiguration : IConfigurationEntity {
	}
	public partial class AudioEncoderConfiguration : IConfigurationEntity {
	}
	public partial class MetadataConfiguration : IConfigurationEntity {
	}
	public partial class VideoOutputConfiguration : IConfigurationEntity {
	}
	//public partial class AudioOutputConfiguration : IConfigurationEntity {
	//}
	//public partial class AudioDecoderConfiguration : IConfigurationEntity {
	//}
	public partial class PTZConfiguration : IConfigurationEntity {
	}
	public partial class VideoAnalyticsConfiguration : IConfigurationEntity {
	}
	public partial class AnalyticsEngine : IConfigurationEntity {
	}
	public partial class AnalyticsEngineInput : IConfigurationEntity {
	}
	public partial class AnalyticsEngineControl : IConfigurationEntity {
	}


	public class QNameListType : IXmlSerializable, IEnumerable<XmlQualifiedName> {
		public QNameListType(IEnumerable<XmlQualifiedName> qnames) {
			m_qnames = qnames.ToArray();
		}
		private static char[] whiteSpaceChars = { ' ', '\t', '\n', '\r' };
		public XmlSchema GetSchema() {
			return null;
		}
		private XmlQualifiedName[] m_qnames = { };
		private IEnumerable<XmlQualifiedName> ParseQNameList(String text, Func<string, string> LookupNs) {
			var qnames = text.Split(whiteSpaceChars, StringSplitOptions.RemoveEmptyEntries);
			foreach (var qn in qnames) {
				//Parse QName
				int si = qn.IndexOf(':');
				string ns;
				string ln;
				if (si != -1) {
					string prefix = qn.Substring(0, si);
					ns = LookupNs(prefix);
					if (ns == null) {
						throw new XmlException(String.Format("prefix {0} in {1} is undefined", prefix, qn));
					}
					ln = qn.Substring(si + 1);
					if (ln == string.Empty) {
						throw new XmlException(String.Format("no local name is defined in qname {0}", qn));
					}
				} else {
					ns = string.Empty;
					ln = qn;
				}
				ln = XmlConvert.DecodeName(ln);
				yield return new XmlQualifiedName(ln, ns);
			}
		}
		public void ReadXml(XmlReader reader) {
			string text = reader.ReadString();
			if (!string.IsNullOrEmpty(text)) {
				m_qnames = ParseQNameList(text, (ns) => reader.LookupNamespace(ns)).ToArray();
			}
			reader.ReadEndElement();
		}

		public void WriteXml(XmlWriter writer) {
			int prefixCount = 0;
			var emptyNsDeclared = false;
			StringBuilder sb = new StringBuilder();
			foreach (var qn in m_qnames) {
				if (sb.Length != 0) {
					sb.Append(' ');
				}
				//SerializationUtility.PrepareQNameString(stringBuilder, ref flag, ref num, writer, current);
				string ln = XmlConvert.EncodeLocalName(qn.Name.Trim());
				string prefix;
				if (qn.Namespace.Length == 0) {
					if (!emptyNsDeclared) {
						writer.WriteAttributeString("xmlns", string.Empty, null, string.Empty);
						emptyNsDeclared = true;
					}
					prefix = null;
				} else {
					prefix = writer.LookupPrefix(qn.Namespace);
					if (prefix == null) {
						prefix = "l" + prefixCount++;
						writer.WriteAttributeString("xmlns", prefix, null, qn.Namespace);
					}
				}
				if (!string.IsNullOrEmpty(prefix)) {
					sb.AppendFormat("{0}:{1}", prefix, ln);
				} else {
					sb.Append(ln);
				}
			}
			writer.WriteString(sb.ToString());
		}

		public IEnumerator<XmlQualifiedName> GetEnumerator() {
			return m_qnames.AsEnumerable().GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return m_qnames.GetEnumerator();
		}
	}

	[XmlRoot(Namespace = "http://www.onvif.org/ver10/schema", IsNullable = false)]
	public partial class SupportedRules {
	}

	[XmlRoot(Namespace = "http://www.onvif.org/ver10/schema", IsNullable = true)]
	public partial class SupportedAnalyticsModules {
	}

	//[Serializable]
	//[XmlType(AnonymousType = true, Namespace = "http://www.onvif.org/ver10/schema")]
	//[XmlRoot(Namespace = "http://www.onvif.org/ver10/schema", IsNullable = false)]
	//public class Message {
	//	public ItemList Source { get; set; }
	//	public ItemList Key { get; set; }
	//	public ItemList Data { get; set; }
	//	public MessageExtension Extension { get; set; }
	//	[XmlAttribute]
	//	public System.DateTime UtcTime { get; set; }
	//	[XmlAttribute]
	//	public PropertyOperation PropertyOperation { get; set; }
	//	[XmlIgnore]
	//	public bool PropertyOperationSpecified { get; set; }
	//	[XmlAnyAttribute]
	//	public XmlAttribute[] AnyAttr { get; set; }
	//}

	//[Serializable]
	//[XmlType(Namespace = "http://www.onvif.org/ver10/schema")]
	//public enum PropertyOperation {
	//	Initialized,
	//	Deleted,
	//	Changed,
	//}

	//[Serializable]
	//[XmlType(Namespace = "http://www.onvif.org/ver10/schema")]
	//[XmlRoot(Namespace = "http://www.onvif.org/ver10/schema", IsNullable = true)]
	//public class MessageExtension {
	//	[XmlAnyElement]
	//	public XmlElement[] Any { get; set; }
	//}

	public partial class Profile {
		public override string ToString() {
			if (String.IsNullOrWhiteSpace(this.name)) {
				return this.token;
			}
			return this.name;
		}
	}
	public partial class PTZSpeed {
		public override string ToString() {
			return new StringBuilder()
				.Append("(pan=")
				.Append(this.panTilt.x.ToString(CultureInfo.InvariantCulture))
				.Append(", tilt=")
				.Append(this.panTilt.y.ToString(CultureInfo.InvariantCulture))
				.Append(", zoom=")
				.Append(this.zoom.x.ToString(CultureInfo.InvariantCulture))
				.Append(")").ToString();
		}
	}
	public partial class FloatRange {
		public override string ToString() {
			return new StringBuilder()
				.Append("(")
				.Append(this.min.ToString(CultureInfo.InvariantCulture))
				.Append(", ")
				.Append(this.max.ToString(CultureInfo.InvariantCulture))
				.Append(")").ToString();
		}
	}

	[XmlRoot(Namespace = "http://www.onvif.org/ver10/schema", IsNullable = true)]
	public partial class IntRectangle {
		public override string ToString() {
			return new StringBuilder()
				.Append("(")
				.Append(x).Append(", ")
				.Append(y).Append(", ")
				.Append(width).Append(", ")
				.Append(height)
				.Append(")").ToString();
		}
	}

	public partial class ConfigurationEntity {
		public override string ToString() {
			if (this.name != null) {
				return this.name;
			}
			return this.token;
		}
	}

	[XmlRoot(Namespace = "http://www.onvif.org/ver10/schema", IsNullable = true)]
	public partial class VideoResolution : IComparable<VideoResolution>, IComparable {
		public override bool Equals(object obj) {
			if (obj == null) {
				return false;
			}
			var other = obj as VideoResolution;
			return other != null && (width == other.width) && (height == other.height);
		}
		public override int GetHashCode() {
			return HashCode.Combine(width, height);
		}
		public override string ToString() {
			return String.Format("{0}x{1}", width, height);
		}

		public int CompareTo(VideoResolution other) {
			if (width != other.width) {
				return width - other.width;
			}
			return height - other.height;
		}

		public int CompareTo(object obj) {
			return CompareTo((VideoResolution)obj);
		}
	}

	/// <remarks/>
	[Serializable]
	[XmlType(Namespace = "http://www.onvif.org/ver10/schema")]
	public partial class AttachmentData{
		[XmlText(DataType = "base64Binary")]
		public byte[] Include;
		[XmlAttribute(Form = XmlSchemaForm.Qualified, Namespace = "http://www.w3.org/2005/05/xmlmime")]
		public string contentType;
	}

	public class XsDuration : IXmlSerializable {
		public TimeSpan timeSpan;

		public XmlSchema GetSchema() {
			return null;
		}

		public static XsDuration FromSeconds(double seconds) {
			return new XsDuration() {
				timeSpan = TimeSpan.FromSeconds(seconds)
			};
		}
		public static implicit operator XsDuration(TimeSpan timeSpan) {
			return new XsDuration() {
				timeSpan = timeSpan
			};
		}

		public static implicit operator TimeSpan(XsDuration duration) {
			return duration.timeSpan;
		}

		public void ReadXml(XmlReader reader) {
			var txt = reader.ReadElementContentAsString();
			if (String.IsNullOrWhiteSpace(txt)) {
				return;
			}
			timeSpan = XmlConvert.ToTimeSpan(txt.Replace(',', '.'));
		}

		public void WriteXml(XmlWriter writer) {
			writer.WriteString(XmlConvert.ToString(timeSpan));
		}
		public string Format() {
			return XmlConvert.ToString(timeSpan);
		}
		public static XsDuration Parse(string value) {
			var duration = new XsDuration();
			duration.timeSpan = XmlConvert.ToTimeSpan(value.Replace(',', '.'));
			return duration;
		}
		public override string ToString() {
			return XmlConvert.ToString(timeSpan);
		}
		public override bool Equals(object obj) {
			var dur = obj as XsDuration;
			return dur != null && timeSpan.Equals(dur.timeSpan);
		}
		public override int GetHashCode() {
			return timeSpan.GetHashCode();
		}

		public static XsDuration FromSeconds(int totalSeconds){
			return new XsDuration(){
				timeSpan = TimeSpan.FromSeconds(totalSeconds)
			};
		}
	}

	/// <remarks/>
	[Serializable]
	[XmlType(AnonymousType=true, TypeName = "QueryExpressionType", Namespace = "http://docs.oasis-open.org/wsn/b-2")]
    public class QueryExpressionType {
		[XmlNamespaceDeclarations]
		public XmlSerializerNamespaces namespaces;

		[XmlText]
		[XmlAnyElement(Order = 0)]
		public XmlNode[] Any;

		[XmlAttribute(DataType = "anyURI")]
		public string Dialect;
	}

	[XmlRoot(ElementName = "MessageContent", Namespace = "http://docs.oasis-open.org/wsn/b-2")]
	public class MessageContentFilter {
		[XmlNamespaceDeclarations]
		public XmlSerializerNamespaces namespaces;
		[XmlText]
		public string expression;
		[XmlAttribute(AttributeName = "Dialect", DataType = "anyURI")]
		public string dialect;
	}

	[XmlRoot(ElementName = "TopicExpression", Namespace = "http://docs.oasis-open.org/wsn/b-2")]
	public class TopicExpressionFilter {
		[XmlNamespaceDeclarations]
		public XmlSerializerNamespaces namespaces;
		[XmlText]
		public string expression;
		[XmlAttribute(AttributeName = "Dialect", DataType = "anyURI")]
		public string dialect;
	}

	[Serializable]
	//[XmlType(Namespace = "http://docs.oasis-open.org/wsn/b-2")]
	public class FilterType : IXmlSerializable {
		public XElement[] Any;
		public void ReadXml(XmlReader reader) {
			var xelist = new LinkedList<XElement>();
			reader.Read();
			var dr = reader as XmlDictionaryReader;
			var gns = reader.GetNamespacesInScope();

			while (reader.NodeType != XmlNodeType.EndElement) {
				if (reader.NodeType == XmlNodeType.Element) {
					var x = XElement.ReadFrom(reader) as XElement;
					foreach (var ns in gns) {
						var pref = ns.Key;
						var uri = ns.Value;
						if (x.GetNamespaceOfPrefix(pref) == null) {
							x.Add(new XAttribute(XName.Get(pref, "http://www.w3.org/2000/xmlns/"), uri));
						}
					}
					xelist.AddLast((XElement)x);
				} else {
					reader.Skip();
				}
			}
			Any = xelist.ToArray();
			reader.ReadEndElement();
		}

		public void WriteXml(XmlWriter writer) {
			if (Any != null) {
				foreach (var x in Any) {
					x.WriteTo(writer);
				}
			}
		}

		public XmlSchema GetSchema() {
			return null;
		}
	}

	public partial class OnvifVersion : IComparable, IComparable<OnvifVersion>, IEquatable<OnvifVersion> {

		public static readonly OnvifVersion v1_2 = new OnvifVersion(1, 2);
		public static readonly OnvifVersion v2_0 = new OnvifVersion(2, 0);
		public static readonly OnvifVersion v2_1 = new OnvifVersion(2, 1);
		public static readonly OnvifVersion v2_2 = new OnvifVersion(2, 2);
		public static readonly OnvifVersion v2_2_1 = new OnvifVersion(2, 21);
		public static readonly OnvifVersion v2_3 = new OnvifVersion(2, 3);

		public OnvifVersion() {
		}

		public OnvifVersion(int Major, int Minor) {
			this._major = Major;
			this._minor = Minor;
		}

		public override bool Equals(object obj) {
			//if (obj == null) {
			//	return false;
			//}
			return Equals(obj as OnvifVersion);
		}

		public override int GetHashCode() {
			return HashCode.Combine(_major, _minor);
		}

		public override string ToString() {
			if (_minor == 0) {
				return String.Format("{0}.0", _major);
			}
			return String.Format("{0}.{1:D2}", _major, _minor);
		}

		private static IEnumerable<int> GetDigitsFromInt(int v) {
			if(v == 0){
				return Enumerable.Empty<int>();
			}
			int d;
			v = Math.DivRem(v, 10, out d);
			return GetDigitsFromInt(v).Append(d);
		}
		public IEnumerable<int> GetDigits() {
			yield return _major;
			foreach(var d in GetDigitsFromInt(_minor)){
				yield return d;
			}
		}

		public int CompareTo(OnvifVersion other) {
			using (var otherDigs = other.GetDigits().GetEnumerator()) {
				foreach (var d in GetDigits()) {
					if (!otherDigs.MoveNext()) {
						return 1;
					}
					var dif = d - otherDigs.Current;
					if (dif != 0) {
						return dif;
					}
				}
				return otherDigs.MoveNext() ? -1 : 0;
			}
		}

		public int CompareTo(object obj) {
			return CompareTo((OnvifVersion)obj);
		}

		public bool Equals(OnvifVersion other) {
			return !Object.ReferenceEquals(other, null) && (_minor == other._minor) && (_major == other._major);
		}

		public static bool operator ==(OnvifVersion v1, OnvifVersion v2) {
			if (Object.ReferenceEquals(v1, null)) {
				return Object.ReferenceEquals(v2, null);
			}
			return v1.Equals(v2);
		}
		public static bool operator !=(OnvifVersion v1, OnvifVersion v2) {
			return !(v1 == v2);
		}
		public static bool operator <(OnvifVersion v1, OnvifVersion v2) {
			return (v1.CompareTo(v2) < 0);
		}
		public static bool operator >(OnvifVersion v1, OnvifVersion v2) {
			return (v1.CompareTo(v2) > 0);
		}
		public static bool operator <=(OnvifVersion v1, OnvifVersion v2) {
			return (v1.CompareTo(v2) <= 0);
		}
		public static bool operator >=(OnvifVersion v1, OnvifVersion v2) {
			return (v1.CompareTo(v2) >= 0);
		}  
	}

	public static class DateTimeExtensions{
		public static System.DateTime? ToSystemDateTime(this DateTime dateTime, DateTimeKind kind = DateTimeKind.Unspecified) {
			if ((object)dateTime == null || (object)dateTime.date == null || (object)dateTime.time == null) {
				return null;
			}
			// seconds can be in range 0..61 (leap seconds), see http://www.onvif.org/onvif/ver10/device/wsdl/devicemgmt.wsdl, section 38
			// leap seconds are not supported in current implementation
			var seconds = dateTime.time.second;
			if(seconds > 59){
				seconds = 59;
			}
			return new System.DateTime(
					dateTime.date.year, 1, 1, 0, 0, seconds, kind
				)
				.AddMonths(dateTime.date.month - 1)
				.AddDays(dateTime.date.day - 1)
				.AddHours(dateTime.time.hour)
				.AddMinutes(dateTime.time.minute);
		}
	}

	// --- ONVIF Media2 service proxy (ver20/media/wsdl) ---
	// Used to query GetVideoEncoderConfigurations from cameras that advertise
	// the Media2 service endpoint (ver20/media/wsdl) via WS-Discovery / GetServices.
	// This is needed for cameras that report Encoding=H264 via Media1 but actually
	// encode H265 — they expose the true encoding only through Media2.

	// IMedia2: All End methods return raw System.ServiceModel.Channels.Message
	// so that WCF never attempts to deserialize the response body. NvtSession.fs reads the
	// body via GetReaderAtBodyContents() and parses with LINQ to XML.
	[ServiceContract(Namespace = "http://www.onvif.org/ver20/media/wsdl")]
	public interface IMedia2 {
		[OperationContract(AsyncPattern = true,
			Action = "http://www.onvif.org/ver20/media/wsdl/GetVideoEncoderConfigurations",
			ReplyAction = "*")]
		IAsyncResult BeginGetVideoEncoderConfigurations(
			Media2GetVideoEncoderConfigurationsRequest request,
			AsyncCallback callback, object asyncState);
		System.ServiceModel.Channels.Message EndGetVideoEncoderConfigurations(
			IAsyncResult result);

		[OperationContract(AsyncPattern = true,
			Action = "http://www.onvif.org/ver20/media/wsdl/GetProfiles",
			ReplyAction = "*")]
		IAsyncResult BeginGetProfiles(
			Media2GetProfilesRequest request,
			AsyncCallback callback, object asyncState);
		System.ServiceModel.Channels.Message EndGetProfiles(
			IAsyncResult result);

		[OperationContract(AsyncPattern = true,
			Action = "http://www.onvif.org/ver20/media/wsdl/GetStreamUri",
			ReplyAction = "*")]
		IAsyncResult BeginGetStreamUri(
			Media2GetStreamUriRequest request,
			AsyncCallback callback, object asyncState);
		System.ServiceModel.Channels.Message EndGetStreamUri(
			IAsyncResult result);

		[OperationContract(AsyncPattern = true,
			Action = "http://www.onvif.org/ver20/media/wsdl/GetVideoEncoderConfigurationOptions",
			ReplyAction = "*")]
		IAsyncResult BeginGetVideoEncoderConfigurationOptions(
			Media2GetVideoEncoderConfigurationOptionsRequest request,
			AsyncCallback callback, object asyncState);
		System.ServiceModel.Channels.Message EndGetVideoEncoderConfigurationOptions(
			IAsyncResult result);

		[OperationContract(AsyncPattern = true,
			Action = "http://www.onvif.org/ver20/media/wsdl/SetVideoEncoderConfigurations",
			ReplyAction = "*")]
		IAsyncResult BeginSetVideoEncoderConfigurations(
			Media2SetVideoEncoderConfigurationsRequest request,
			AsyncCallback callback, object asyncState);
		System.ServiceModel.Channels.Message EndSetVideoEncoderConfigurations(
			IAsyncResult result);

		[OperationContract(AsyncPattern = true,
			Action = "http://www.onvif.org/ver20/media/wsdl/GetVideoSourceConfigurations",
			ReplyAction = "*")]
		IAsyncResult BeginGetVideoSourceConfigurations(
			Media2GetVideoSourceConfigurationsRequest request,
			AsyncCallback callback, object asyncState);
		System.ServiceModel.Channels.Message EndGetVideoSourceConfigurations(
			IAsyncResult result);

		[OperationContract(AsyncPattern = true,
			Action = "http://www.onvif.org/ver20/media/wsdl/GetSnapshotUri",
			ReplyAction = "*")]
		IAsyncResult BeginGetSnapshotUri(
			Media2GetSnapshotUriRequest request,
			AsyncCallback callback, object asyncState);
		System.ServiceModel.Channels.Message EndGetSnapshotUri(
			IAsyncResult result);
	}

	[MessageContract(WrapperName = "GetVideoEncoderConfigurations",
		WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl", IsWrapped = true)]
	public partial class Media2GetVideoEncoderConfigurationsRequest {
		public Media2GetVideoEncoderConfigurationsRequest() { }
	}

	[MessageContract(WrapperName = "GetProfiles",
		WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl", IsWrapped = true)]
	public partial class Media2GetProfilesRequest {
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 0)]
		public string Token;
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 1)]
		public string Type;
		public Media2GetProfilesRequest() { }
	}

	[MessageContract(WrapperName = "GetStreamUri",
		WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl", IsWrapped = true)]
	public partial class Media2GetStreamUriRequest {
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 0)]
		public string ProfileToken;
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 1)]
		public string Protocol;
		public Media2GetStreamUriRequest() { }
	}

	[MessageContract(WrapperName = "GetVideoEncoderConfigurationOptions",
		WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl", IsWrapped = true)]
	public partial class Media2GetVideoEncoderConfigurationOptionsRequest {
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 0)]
		public string ConfigurationToken;
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 1)]
		public string ProfileToken;
		public Media2GetVideoEncoderConfigurationOptionsRequest() { }
	}

	[MessageContract(WrapperName = "SetVideoEncoderConfigurations",
		WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl", IsWrapped = true)]
	public partial class Media2SetVideoEncoderConfigurationsRequest {
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 0)]
		public System.Xml.Linq.XElement Configuration;
		public Media2SetVideoEncoderConfigurationsRequest() { }
	}

	[MessageContract(WrapperName = "GetVideoSourceConfigurations",
		WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl", IsWrapped = true)]
	public partial class Media2GetVideoSourceConfigurationsRequest {
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 0)]
		public string ConfigurationToken;
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 1)]
		public string ProfileToken;
		public Media2GetVideoSourceConfigurationsRequest() { }
	}

	[MessageContract(WrapperName = "GetSnapshotUri",
		WrapperNamespace = "http://www.onvif.org/ver20/media/wsdl", IsWrapped = true)]
	public partial class Media2GetSnapshotUriRequest {
		[MessageBodyMember(Namespace = "http://www.onvif.org/ver20/media/wsdl", Order = 0)]
		public string ProfileToken;
		public Media2GetSnapshotUriRequest() { }
	}

	// Plain data class populated by LINQ-to-XML parser in NvtSession.fs
	// Represents per-encoding options returned by Media2 GetVideoEncoderConfigurationOptions.
	public class Media2EncoderOptions {
		public string Encoding { get; set; }
		public VideoResolution[] ResolutionsAvailable { get; set; }
		public IntRange GovLengthRange { get; set; }   // nullable — not all encodings have it
		public IntRange FrameRateRange { get; set; }
		public IntRange BitrateRange { get; set; }
	}

	// Static XML parsing helpers for Media2 response bodies.
	// Methods accept the string produced by response.GetReaderAtBodyContents().ReadOuterXml()
	// and return parsed ONVIF types. Exposed as static methods for unit testability.
	public static class Media2XmlParser {
		private static readonly XNamespace NsTr2 = XNamespace.Get("http://www.onvif.org/ver20/media/wsdl");
		private static readonly XNamespace NsTt  = XNamespace.Get("http://www.onvif.org/ver10/schema");

		/// <summary>
		/// Parses the body XML from a Media2 GetProfiles response into Profile[].
		/// </summary>
		public static Profile[] ParseGetProfilesResponse(string bodyXml) {
			if (string.IsNullOrWhiteSpace(bodyXml)) return new Profile[0];
			var doc = XDocument.Parse(bodyXml);
			var result = new List<Profile>();
			foreach (var el in doc.Root.Elements(NsTr2 + "Profiles")) {
				var tokenAttr = el.Attribute("token");
				if (tokenAttr == null || string.IsNullOrEmpty(tokenAttr.Value)) continue;
				var profile = new Profile { token = tokenAttr.Value };

				var nameEl = el.Element(NsTt + "Name");
				if (nameEl != null) profile.name = nameEl.Value;

				var vscEl = el.Element(NsTt + "VideoSourceConfiguration");
				if (vscEl != null) {
					var vsc = new VideoSourceConfiguration();
					var vscToken = vscEl.Attribute("token");
					if (vscToken != null) vsc.token = vscToken.Value;
					var vscName = vscEl.Element(NsTt + "Name");
					if (vscName != null) vsc.name = vscName.Value;
					var srcTok = vscEl.Element(NsTt + "SourceToken");
					if (srcTok != null) vsc.sourceToken = srcTok.Value;
					profile.videoSourceConfiguration = vsc;
				}

				var vecEl = el.Element(NsTt + "VideoEncoderConfiguration");
				if (vecEl != null)
					profile.videoEncoderConfiguration = ParseVideoEncoderConfigElement(vecEl);

				result.Add(profile);
			}
			return result.ToArray();
		}

		/// <summary>
		/// Parses a single tt:VideoEncoderConfiguration XElement.
		/// </summary>
		public static VideoEncoderConfiguration ParseVideoEncoderConfigElement(XElement el) {
			var cfg = new VideoEncoderConfiguration();
			var tokenAttr = el.Attribute("token");
			if (tokenAttr != null) cfg.token = tokenAttr.Value;

			var nameEl = el.Element(NsTt + "Name");
			if (nameEl != null) cfg.name = nameEl.Value;

			var encEl = el.Element(NsTt + "Encoding");
			if (encEl != null) {
				switch (encEl.Value.Trim().ToUpperInvariant()) {
					case "H265":  cfg.encoding = VideoEncoding.h265; break;
					case "JPEG":  cfg.encoding = VideoEncoding.jpeg; break;
					case "MPEG4": cfg.encoding = VideoEncoding.mpeg4; break;
					default:      cfg.encoding = VideoEncoding.h264; break;
				}
			}

			var resEl = el.Element(NsTt + "Resolution");
			if (resEl != null) {
				var wEl = resEl.Element(NsTt + "Width");
				var hEl = resEl.Element(NsTt + "Height");
				if (wEl != null && hEl != null) {
					cfg.resolution = new VideoResolution {
						width  = int.Parse(wEl.Value),
						height = int.Parse(hEl.Value)
					};
				}
			}

			var rcEl = el.Element(NsTt + "RateControl");
			if (rcEl != null) {
				var rc = new VideoRateControl();
				var frlEl = rcEl.Element(NsTt + "FrameRateLimit");
				var bilEl = rcEl.Element(NsTt + "BitrateLimit");
				var eiEl  = rcEl.Element(NsTt + "EncodingInterval");
				if (frlEl != null) rc.frameRateLimit    = int.Parse(frlEl.Value);
				if (bilEl != null) rc.bitrateLimit      = int.Parse(bilEl.Value);
				if (eiEl  != null) rc.encodingInterval  = int.Parse(eiEl.Value);
				cfg.rateControl = rc;
			}

			var h264El = el.Element(NsTt + "H264");
			if (h264El != null) {
				var h264 = new H264Configuration();
				var govEl = h264El.Element(NsTt + "GovLength");
				if (govEl != null) h264.govLength = int.Parse(govEl.Value);
				cfg.h264 = h264;
			}

			var h265El = el.Element(NsTt + "H265");
			if (h265El != null) {
				var h265 = new H265Configuration();
				var govEl = h265El.Element(NsTt + "GovLength");
				if (govEl != null) h265.govLength = int.Parse(govEl.Value);
				cfg.h265 = h265;
			}

			return cfg;
		}

		/// <summary>
		/// Parses the body XML from a Media2 GetStreamUri response.
		/// Returns the URI string, or null if the element is absent or empty.
		/// </summary>
		public static string ParseGetStreamUriResponse(string bodyXml) {
			if (string.IsNullOrWhiteSpace(bodyXml)) return null;
			var doc = XDocument.Parse(bodyXml);
			var uriEl = doc.Root.Element(NsTr2 + "Uri");
			if (uriEl == null || string.IsNullOrEmpty(uriEl.Value)) return null;
			return uriEl.Value;
		}
	}
}