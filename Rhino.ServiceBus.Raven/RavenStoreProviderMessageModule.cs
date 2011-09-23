using System;
using Raven.Client;
using Rhino.ServiceBus.Impl;
using Rhino.ServiceBus.Internal;
using Rhino.ServiceBus.MessageModules;

namespace Rhino.ServiceBus.Raven
{
    public class RavenStoreProviderMessageModule : IMessageModule, IDocumentStoreProvider
    {
        private readonly IDocumentStore store;

        public RavenStoreProviderMessageModule(IDocumentStore store)
        {
            this.store = store;
        }

        [ThreadStatic]
        private static IDocumentSession session;

        [ThreadStatic]
        private static bool messageArrived;

        public void Init(ITransport transport, IServiceBus bus)
        {
            transport.BeforeMessageTransactionCommit += BeforeMessageTransactionCommit;
            transport.MessageArrived += TransportMessageArrived;
            transport.MessageProcessingFailure += TransportMessageProcessingCompleted;
            transport.MessageProcessingCompleted += TransportMessageProcessingCompleted;
            transport.MessageSerializationException += TransportMessageProcessingCompleted;
        }

        private static void BeforeMessageTransactionCommit(CurrentMessageInformation obj)
        {
            if (session != null)
                session.SaveChanges();
        }

        private static void TransportMessageProcessingCompleted(CurrentMessageInformation arg1, Exception arg2)
        {
            messageArrived = false;
            if (session == null)
                return;
            session.Dispose();
            session = null;
        }

        private static bool TransportMessageArrived(CurrentMessageInformation arg)
        {
            messageArrived = true;
            return false;
        }

        private IDocumentSession CreateSession()
        {
            if (messageArrived == false)
                throw new InvalidOperationException("Cannot use session outside of message processing.");

            session = store.OpenSession();
            session.Advanced.UseOptimisticConcurrency = true;
            return session;
        }

        public void Stop(ITransport transport, IServiceBus bus)
        {
            transport.BeforeMessageTransactionCommit -= BeforeMessageTransactionCommit;
            transport.MessageArrived -= TransportMessageArrived;
            transport.MessageProcessingCompleted -= TransportMessageProcessingCompleted;
            transport.MessageProcessingFailure -= TransportMessageProcessingCompleted;
            transport.MessageSerializationException -= TransportMessageProcessingCompleted;
        }

        public IDocumentSession Current
        {
            get { return session ?? CreateSession(); }
        }
    }
}