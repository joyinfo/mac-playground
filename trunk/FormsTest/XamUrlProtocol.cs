﻿#if MAC && XAMARINMAC

using System;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

namespace FormsTest
{
	public class UrlProtocol : NSUrlProtocol
	{
		static NSObject BeingHandledValue = new NSString("YES");
		static string BeingHandledKey = "BeingHandledByUrlProtocol";
		static Class urlProtocolClass;

		NSUrlConnection connection;
		internal NSUrlProtocolClient Client;

		public static void Register()
		{
			urlProtocolClass = new Class(typeof(UrlProtocol));
			NSUrlProtocol.RegisterClass(urlProtocolClass);
		}

		public static void Unregister()
		{
			if (urlProtocolClass != null)
				NSUrlProtocol.UnregisterClass(urlProtocolClass);
		}

		[Export("canInitWithRequest:")]
		public static bool canInitWithRequest(NSUrlRequest request)
		{
			// request.Url.Scheme == "custom";

			Console.WriteLine("canInitWithRequest: " + request.Url);

			if (NSUrlProtocol.GetProperty(BeingHandledKey, request) != null)
				return false;

			return true;
		}

		[Export("canonicalRequestForRequest:")]
		public static new NSUrlRequest GetCanonicalRequest(NSUrlRequest forRequest)
		{
			return forRequest;
		}

		[Export("initWithRequest:cachedResponse:client:")]
		public UrlProtocol(NSUrlRequest request, NSCachedUrlResponse cachedResponse, NSUrlProtocolClient client)
			: base(request, cachedResponse, client)
		{
			this.Client = client;
		}

		public override void StartLoading()
		{
			Console.WriteLine("StartLoading: " + this.Request.Url.AbsoluteString);

			var request = (this.Request as NSMutableUrlRequest) ?? (NSMutableUrlRequest)this.Request.MutableCopy();
			NSUrlProtocol.SetProperty(BeingHandledValue, BeingHandledKey, request);
			connection = new NSUrlConnection(request, new UrlConnectionDelegate(this), true);

			//using (var response = new NSUrlResponse(Request.Url, "text/html", -1, null))
			//{
			//	using (var data = NSData.FromString("<html><body><h1>XamUrlProcotol</h1><p>This page was generated by XamUrlProtocol.</p><body></html>"))
			//	{
			//		client.ReceivedResponse(this, response, NSUrlCacheStoragePolicy.NotAllowed);
			//		client.DataLoaded(this, data);
			//		client.FinishedLoading(this);
			//	}
			//}
		}

		public override void StopLoading()
		{
			if (connection != null)
			{
				connection.Cancel();
				connection = null;
			}
		}

	}

	public class UrlConnectionDelegate : NSObject, INSUrlConnectionDataDelegate, INSUrlConnectionDelegate
	{
		UrlProtocol handler;

		public UrlConnectionDelegate(UrlProtocol handler)
			: base()
		{
			this.handler = handler;
		}

		[Export("connection:didReceiveResponse:")]
		public void ReceivedResponse(NSUrlConnection connection, NSUrlResponse response)
		{
			Console.WriteLine("ReceivedResponse");
			handler.Client.ReceivedResponse(handler, response, NSUrlCacheStoragePolicy.NotAllowed);
		}

		[Export("connection:didReceiveData:")]
		public void ReceivedData(NSUrlConnection connection, NSData data)
		{
			Console.WriteLine("ReceivedData");
			handler.Client.DataLoaded(handler, data);
		}

		[Export("connectionDidFinishLoading:")]
		public void FinishedLoading(NSUrlConnection connection)
		{
			Console.WriteLine("FinishedLoading");
			handler.Client.FinishedLoading(handler);
		}

		[Export("connection:didFailWithError:")]
		public void FailedWithError(NSUrlConnection connection, NSError error)
		{
			Console.WriteLine($"FailedWithError: url='{connection}', error='{error.Description}'");
			handler.Client.FailedWithError(handler, error);
		}
	}
}

#endif
