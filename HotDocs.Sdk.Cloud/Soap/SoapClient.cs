﻿/* Copyright (c) 2013, HotDocs Limited
   Use, modification and redistribution of this source is subject
   to the New BSD License as set out in LICENSE.TXT. */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Net;
using System.IO;
using HotDocs.Sdk.Server;
using HotDocs.Sdk.Server.Contracts;

namespace HotDocs.Sdk.Cloud
{
	/// <summary>
	/// The default Core Services client.
	/// This implementation uses the Core Services SOAP web service.
	/// </summary>
	public class SoapClient : ClientBase, IDisposable
	{
		#region Private fields
		private Proxy _proxy;
		#endregion

		#region Constructors
		/// <summary>
		/// Constructs a Client object using 
		/// </summary>
		/// <param name="subscriberId">Subscriber ID provided by HotDocs</param>
		/// <param name="signingKey">Signing key provided by HotDocs</param>
		/// <param name="hostAddress">Optional service host address -- overrides the config file</param>
		/// <param name="proxyServerAddress">Optional proxy server address -- overrides the config file</param>
		public SoapClient(
			string subscriberId,
			string signingKey,
			string hostAddress = null,
			string proxyServerAddress = null)
			: base(subscriberId, signingKey, hostAddress, "/Core.svc", proxyServerAddress)
		{
			try
			{
				// The consumer has the option of configuring this client in their config file.
				// If there is no appropriate endpoint element in the config file, we catch the
				// resulting exception and create the proxy using hard-coded settings.
				_proxy = new Proxy();

				if (!string.IsNullOrEmpty(hostAddress))
				{
					_proxy.Endpoint.Address = new EndpointAddress(EndpointAddress);
				}
				else
				{
					EndpointAddress = _proxy.Endpoint.Address.Uri.AbsolutePath;
				}
			}
			catch (InvalidOperationException) // No configuration provided by the consumer
			{
				_proxy = new Proxy(GetDefaultBinding(), new EndpointAddress(EndpointAddress));
			}

			if (!string.IsNullOrEmpty(proxyServerAddress))
			{
				// Find the HttpTransportBindingElement binding element and set the proxy properties
				var binding = (CustomBinding)_proxy.Endpoint.Binding;
				var transport = (HttpTransportBindingElement)binding.Elements.Single(e => e is HttpTransportBindingElement);
				transport.UseDefaultWebProxy = false;
				transport.ProxyAddress = new Uri(proxyServerAddress);
				transport.BypassProxyOnLocal = false;
			}

			SetTcpKeepAlive();
#if DEBUG
			// For debug builds, allow invalid server certificates.
			ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
#endif
		}
		#endregion

		#region Protected internal implementations of abstract methods from the base class

		/// <summary>
		/// 
		/// </summary>
		/// <param name="template"></param>
		/// <param name="answers"></param>
		/// <param name="options"></param>
		/// <param name="billingRef"></param>
		/// <param name="uploadPackage"></param>
		/// <returns></returns>
		protected internal override AssemblyResult AssembleDocumentImpl(
			Template template,
			string answers,
			AssembleDocumentSettings options,
			string billingRef,
			bool uploadPackage)
		{
			var timestamp = DateTime.UtcNow;
			if (!(template.Location is PackageTemplateLocation))
				throw new Exception("HotDocs Cloud Services requires the use of template packages. Please use a PackageTemplateLocation derivative.");
			PackageTemplateLocation packageTemplateLocation = (PackageTemplateLocation)template.Location;

			// Determine the output format to use (e.g., translate options.Type to Contracts.InterviewFormat)
			OutputFormat outputFormat;
			switch (options.Format)
			{
				case DocumentType.HFD:
					outputFormat = OutputFormat.HFD;
					break;
				case DocumentType.HPD:
					outputFormat = OutputFormat.HPD;
					break;
				case DocumentType.HTML:
					outputFormat = OutputFormat.HTML;
					break;
				case DocumentType.HTMLwDataURIs:
					outputFormat = OutputFormat.HTMLwDataURIs;
					break;
				case DocumentType.MHTML:
					outputFormat = OutputFormat.MHTML;
					break;
				case DocumentType.Native:
					outputFormat = OutputFormat.Native;
					break;
				case DocumentType.PDF:
					outputFormat = OutputFormat.PDF;
					break;
				case DocumentType.PlainText:
					outputFormat = OutputFormat.PlainText;
					break;
				case DocumentType.WordDOC:
					outputFormat = OutputFormat.DOCX;
					break;
				case DocumentType.WordDOCX:
					outputFormat = OutputFormat.DOCX;
					break;
				case DocumentType.WordPerfect:
					outputFormat = OutputFormat.WPD;
					break;
				case DocumentType.WordRTF:
					outputFormat = OutputFormat.RTF;
					break;
				default:
					outputFormat = OutputFormat.None;
					break;
			}

			// Always include the Answers output
			outputFormat |= OutputFormat.Answers;

			string hmac = HMAC.CalculateHMAC(
				SigningKey,
				timestamp,
				SubscriberId,
				packageTemplateLocation.PackageID,
				template.FileName,
				uploadPackage,
				billingRef,
				outputFormat,
				options.Settings);

			return _proxy.AssembleDocument(
				SubscriberId,
				packageTemplateLocation.PackageID,
				template.FileName,
				GetBinaryObjectArrayFromString(answers),
				outputFormat,
				options.Settings,
				billingRef,
				timestamp,
				GetPackageIfNeeded(packageTemplateLocation, uploadPackage),
				hmac);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="template"></param>
		/// <param name="answers"></param>
		/// <param name="options"></param>
		/// <param name="billingRef"></param>
		/// <param name="uploadPackage"></param>
		/// <returns></returns>
		protected internal override BinaryObject[] GetInterviewImpl(
			Template template,
			string answers,
			InterviewSettings options,
			string billingRef,
			bool uploadPackage)
		{
			var timestamp = DateTime.UtcNow;
			if (!(template.Location is PackageTemplateLocation))
				throw new Exception("HotDocs Cloud Services requires the use of template packages. Please use a PackageTemplateLocation derivative.");
			PackageTemplateLocation packageTemplateLocation = (PackageTemplateLocation)template.Location;

			// Workaround for bug in server that does not honor the Disable settings, so we have to just clear the url instead.
			// To do this, we make a copy of the settings that were given to us, modify them, and then use the modified version
			// in the call to Cloud Services.
			// TODO: After TFS #5598 is fixed, we can remove this workaround.
			Dictionary<string, string> settingsDict = new Dictionary<string, string>(options.Settings);
			if (options.DisableDocumentPreview == Tristate.True)
				settingsDict.Remove("DocPreviewUrl");
			if (options.DisableSaveAnswers == Tristate.True)
				settingsDict.Remove("SaveAnswersPageUrl");

			string hmac = HMAC.CalculateHMAC(
				SigningKey,
				timestamp,
				SubscriberId,
				packageTemplateLocation.PackageID,
				template.FileName,
				uploadPackage,
				billingRef,
				options.Format,
				options.InterviewImageUrl,
				settingsDict);

			return _proxy.GetInterview(
				SubscriberId,
				packageTemplateLocation.PackageID,
				template.FileName,
				GetBinaryObjectArrayFromString(answers),
				options.Format,
				options.MarkedVariables,
				options.InterviewImageUrl,
				settingsDict,
				billingRef,
				timestamp,
				GetPackageIfNeeded(packageTemplateLocation, uploadPackage),
				hmac);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="template"></param>
		/// <param name="includeDialogs"></param>
		/// <param name="billingRef"></param>
		/// <param name="uploadPackage"></param>
		/// <returns></returns>
		protected internal override ComponentInfo GetComponentInfoImpl(
			Template template,
			bool includeDialogs,
			string billingRef,
			bool uploadPackage)
		{
			var timestamp = DateTime.UtcNow;
			if (!(template.Location is PackageTemplateLocation))
				throw new Exception("HotDocs Cloud Services requires the use of template packages. Please use a PackageTemplateLocation derivative.");
			PackageTemplateLocation packageTemplateLocation = (PackageTemplateLocation)template.Location;

			string hmac = HMAC.CalculateHMAC(
				SigningKey,
				timestamp,
				SubscriberId,
				packageTemplateLocation.PackageID,
				template.FileName,
				uploadPackage,
				billingRef,
				includeDialogs);

			return _proxy.GetComponentInfo(
				SubscriberId,
				packageTemplateLocation.PackageID,
				template.FileName,
				includeDialogs,
				billingRef,
				timestamp,
				GetPackageIfNeeded(packageTemplateLocation, uploadPackage),
				hmac);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="answers"></param>
		/// <param name="billingRef"></param>
		/// <returns></returns>
		protected internal override BinaryObject GetAnswersImpl(BinaryObject[] answers, string billingRef)
		{
			DateTime timestamp = DateTime.UtcNow;

			string hmac = HMAC.CalculateHMAC(
				SigningKey,
				timestamp,
				SubscriberId,
				billingRef);

			return _proxy.GetAnswers(
				SubscriberId,
				answers.ToArray(),
				billingRef,
				timestamp,
				hmac);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="func"></param>
		/// <returns></returns>
		protected internal override object TryWithoutAndWithPackage(Func<bool, object> func)
		{
			try
			{
				// Try without the package first.
				// The "false" may be overridden by the NewPackage property of the Template object.
				return func(false);
			}
			catch (FaultException<ExceptionDetail> ex)
			{
				if (ex.Detail.Type == "HotDocs.Cloud.Storage.PackageNotFoundException")
				{
					return func(true);
				}
				throw;
			}
		}
		#endregion

		#region Private methods

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		private Binding GetDefaultBinding()
		{
			var mtom = new MtomMessageEncodingBindingElement();
			mtom.MaxBufferSize = 1073741824;
			mtom.ReaderQuotas.MaxDepth = 256;
			mtom.ReaderQuotas.MaxStringContentLength = 1073741824;
			mtom.ReaderQuotas.MaxArrayLength = 1073741824;
			mtom.ReaderQuotas.MaxBytesPerRead = 1073741824;
			mtom.ReaderQuotas.MaxNameTableCharCount = 1073741824;

			var transport = new HttpsTransportBindingElement();
			transport.MaxReceivedMessageSize = 1073741824;
			transport.MaxBufferSize = 1073741824;
			transport.MaxBufferPoolSize = 1073741824;
			transport.KeepAliveEnabled = false;

			var binding = new CustomBinding(mtom, transport);
			binding.CloseTimeout = TimeSpan.FromMinutes(5);
			binding.OpenTimeout = TimeSpan.FromMinutes(5);
			binding.ReceiveTimeout = TimeSpan.FromMinutes(20);
			binding.SendTimeout = TimeSpan.FromMinutes(20);

			return binding;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="str"></param>
		/// <returns></returns>
		private BinaryObject[] GetBinaryObjectArrayFromString(string str)
		{
			if (str != null)
			{
				return new BinaryObject[]
				{
					new BinaryObject
					{
						Data = Encoding.UTF8.GetBytes(str),
						DataEncoding = "UTF-8"
					}
				};
			}
			return null;
		}
		#endregion

		#region Dispose methods
		// Here we implement the dispose pattern to override the default disposal behavior.
		// We do this because the default disposal behavior leaves the proxy open if
		// Close() throws an exception.  We want to make sure the proxy always closes or aborts.

		/// <summary>
		/// Disposes the client.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Disposes the client.
		/// </summary>
		/// <param name="disposing"></param>
		protected virtual void Dispose(bool disposing)
		{
			if (_proxy != null)
			{
				if (disposing)
				{
					if (_proxy.State != CommunicationState.Closed && _proxy.State != CommunicationState.Faulted)
					{
						try
						{
							_proxy.Close();
						}
						catch (Exception)
						{
							_proxy.Abort();
							throw;
						}
					}
					else
					{
						_proxy.Abort();
					}
				}
				_proxy = null;
			}
		}

		/// <summary>
		/// Finalizer for the client.
		/// </summary>
		~SoapClient()
		{
			Dispose(false);
		}
		#endregion
	}
}