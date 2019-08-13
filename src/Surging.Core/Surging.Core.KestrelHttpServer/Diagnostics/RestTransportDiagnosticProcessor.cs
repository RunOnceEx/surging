﻿using Surging.Core.CPlatform.Diagnostics;
using Surging.Core.CPlatform.Messages;
using Surging.Core.CPlatform.Serialization;
using Surging.Core.CPlatform.Utilities;
using System;
using System.Collections.Concurrent;
using SurgingEvents = Surging.Core.CPlatform.Diagnostics.DiagnosticListenerExtensions;

namespace Surging.Core.KestrelHttpServer.Diagnostics
{
   public class RestTransportDiagnosticProcessor : ITracingDiagnosticProcessor
    {
        private Func<TransportEventData, string> _transportOperationNameResolver;
        public string ListenerName => SurgingEvents.DiagnosticListenerName;


        private readonly ConcurrentDictionary<string, SegmentContext> _resultDictionary =
            new ConcurrentDictionary<string, SegmentContext>();

        private readonly ISerializer<string> _serializer;

        public Func<TransportEventData, string> TransportOperationNameResolver
        {
            get
            {
                return _transportOperationNameResolver ??
                       (_transportOperationNameResolver = (data) => "Rest-Transport:: " + data.Message.MessageName);
            }
            set => _transportOperationNameResolver =
                value ?? throw new ArgumentNullException(nameof(TransportOperationNameResolver));
        }

        private readonly ITracingContext _tracingContext;
        private readonly IEntrySegmentContextAccessor _entrySegmentContextAccessor;
        private readonly IExitSegmentContextAccessor _exitSegmentContextAccessor;

        public RestTransportDiagnosticProcessor(ITracingContext tracingContext,
            IEntrySegmentContextAccessor entrySegmentContextAccessor,
            IExitSegmentContextAccessor exitSegmentContextAccessor, ISerializer<string> serializer)
        {
            _tracingContext = tracingContext;
            _exitSegmentContextAccessor = exitSegmentContextAccessor;
            _entrySegmentContextAccessor = entrySegmentContextAccessor;
            _serializer = serializer;
        }

        [DiagnosticName(SurgingEvents.SurgingBeforeTransport, TransportType.Rest)]
        public void TransportBefore([Object] TransportEventData eventData)
        {
            var message = eventData.Message.GetContent<HttpMessage>();
            var operationName = TransportOperationNameResolver(eventData);
            var context = _tracingContext.CreateEntrySegmentContext(operationName,
                new RestRequestCarrierHeaderCollection(eventData.Headers));
            context.Span.AddLog(LogEvent.Message($"Worker running at: {DateTime.Now}"));
            context.Span.SpanLayer = SpanLayer.HTTP;
            context.Span.Peer = new StringOrIntValue(eventData.RemoteAddress);
            _resultDictionary.TryAdd(eventData.OperationId.ToString(), context);
            context.Span.AddTag(Tags.REST_METHOD, eventData.Method.ToString());
            context.Span.AddTag(Tags.REST_PARAMETERS, _serializer.Serialize(message.Parameters));
            context.Span.AddTag(Tags.REST_LOCAL_ADDRESS, NetUtils.GetHostAddress().ToString());
        }

        [DiagnosticName(SurgingEvents.SurgingAfterTransport, TransportType.Rest)]
        public void TransportAfter([Object] ReceiveEventData eventData)
        {
            _resultDictionary.TryRemove(eventData.OperationId.ToString(), out SegmentContext context);
            if (context != null)
            {
                _tracingContext.Release(context);
            }
        }

        [DiagnosticName(SurgingEvents.SurgingErrorTransport, TransportType.Rest)]
        public void TransportErrorPublish([Object] TransportErrorEventData eventData)
        {
            _resultDictionary.TryRemove(eventData.OperationId.ToString(), out SegmentContext context);
            if (context != null)
            {
                context.Span.ErrorOccurred(eventData.Exception);
                _tracingContext.Release(context);
            }
        }
    }
}