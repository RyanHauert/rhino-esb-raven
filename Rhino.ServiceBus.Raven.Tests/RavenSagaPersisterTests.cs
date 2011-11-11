using System;
using System.Collections.Generic;
using System.Threading;
using Castle.Windsor;
using Raven.Client;
using Raven.Client.Embedded;
using Rhino.ServiceBus.Impl;
using Rhino.ServiceBus.Internal;
using Rhino.ServiceBus.MessageModules;
using Rhino.ServiceBus.Raven;
using Rhino.ServiceBus.Sagas;
using Xunit;
using Component = Castle.MicroKernel.Registration.Component;

namespace Rhino.ServiceBus.Tests
{
    public class SagaTests
    {
        private static Guid sagaId;
        private static ManualResetEvent wait;
        private readonly IWindsorContainer container;

        public SagaTests()
        {
            OrderProcessor.LastState = null;
            wait = new ManualResetEvent(false);

            container = new WindsorContainer();
            container.Register(
                Component.For<IDocumentStore>()
                    .Instance(StartupDb()),
                Component.For<IMessageModule, IDocumentStoreProvider>()
                    .ImplementedBy<RavenStoreProviderMessageModule>(),
                Component.For(typeof(ISagaPersister<>))
                    .ImplementedBy(typeof(RavenSagaPersister<>)),
                Component.For<OrderProcessor>()
                );
            new RhinoServiceBusConfiguration()
                .UseCastleWindsor(container)
                .Configure();
        }


        public static IDocumentStore StartupDb()
        {
            //NonAdminHttp.EnsureCanListenToWhenInNonAdminContext(8080);
            var store = new EmbeddableDocumentStore
            {
                //DataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Raven"),
                RunInMemory = true,
            };
            store.Initialize();
            return store;
        }

        [Fact]
        public void when_sending_non_initiating_message_saga_will_not_be_invoked()
        {
            using (var bus = container.Resolve<IStartableServiceBus>())
            {
                var transport = container.Resolve<ITransport>();
                bus.Start();

                transport.MessageProcessingCompleted += (i, e) => wait.Set();
                bus.Send(bus.Endpoint, new AddLineItemMessage());
                wait.WaitOne(TimeSpan.FromSeconds(30), false);

                Assert.Null(OrderProcessor.LastState);
            }
        }

        [Fact]
        public void When_saga_with_same_correlation_id_exists_and_get_initiating_message_will_usage_same_saga()
        {
            using (var bus = container.Resolve<IStartableServiceBus>())
            {
                bus.Start();

                var guid = Guid.NewGuid();

                bus.Send(bus.Endpoint, new NewOrderMessage { CorrelationId = guid });
                wait.WaitOne(TimeSpan.FromSeconds(30), false);

                wait.Reset();

                bus.Send(bus.Endpoint, new NewOrderMessage { CorrelationId = guid });
                wait.WaitOne(TimeSpan.FromSeconds(30), false);

                Assert.Equal(2, OrderProcessor.LastState.Count);
            }
        }

        [Fact]
        public void Can_create_saga_entity()
        {
            using (var bus = container.Resolve<IStartableServiceBus>())
            {
                bus.Start();

                bus.Send(bus.Endpoint, new NewOrderMessage());
                wait.WaitOne(TimeSpan.FromSeconds(30), false);

                var storeProvider = new TestDocumentStoreProvider(container.Resolve<IDocumentStore>());
                var persister = new RavenSagaPersister<OrderProcessor>(storeProvider, container.Resolve<IServiceLocator>(), container.Resolve<IReflection>());
                OrderProcessor processor = null;
                while (processor == null)
                {
                    Thread.Sleep(500);
                    processor = persister.Get(sagaId);
                }

                Assert.Equal(1, processor.State.Count);
            }
        }

        [Fact]
        public void When_creating_saga_entity_will_set_saga_id()
        {
            using (var bus = container.Resolve<IStartableServiceBus>())
            {
                bus.Start();

                bus.Send(bus.Endpoint, new NewOrderMessage2());
                wait.WaitOne(TimeSpan.FromSeconds(30), false);

                var storeProvider = new TestDocumentStoreProvider(container.Resolve<IDocumentStore>());
                var persister = new RavenSagaPersister<OrderProcessor>(storeProvider, container.Resolve<IServiceLocator>(), container.Resolve<IReflection>());
                OrderProcessor processor = null;
                while (processor == null)
                {
                    Thread.Sleep(500);
                    processor = persister.Get(sagaId);
                }

                Assert.NotEqual(Guid.Empty, sagaId);
            }
        }

        [Fact]
        public void Can_send_several_messages_to_same_instance_of_saga_entity()
        {
            using (var bus = container.Resolve<IStartableServiceBus>())
            {
                bus.Start();

                bus.Send(bus.Endpoint, new NewOrderMessage());
                wait.WaitOne(TimeSpan.FromSeconds(30), false);
                wait.Reset();

                Assert.Equal(1, OrderProcessor.LastState.Count);

                bus.Send(bus.Endpoint, new AddLineItemMessage { CorrelationId = sagaId });

                wait.WaitOne(TimeSpan.FromSeconds(30), false);

                Assert.Equal(2, OrderProcessor.LastState.Count);
            }
        }

        [Fact]
        public void Completing_saga_will_get_it_out_of_the_in_memory_persister()
        {
            using (var bus = container.Resolve<IStartableServiceBus>())
            {
                bus.Start();

                bus.Send(bus.Endpoint, new NewOrderMessage());
                wait.WaitOne(TimeSpan.FromSeconds(30), false);
                wait.Reset();

                var storeProvider = new TestDocumentStoreProvider(container.Resolve<IDocumentStore>());
                var persister = new RavenSagaPersister<OrderProcessor>(storeProvider, container.Resolve<IServiceLocator>(), container.Resolve<IReflection>());
                OrderProcessor processor = null;
                while (processor == null)
                {
                    Thread.Sleep(500);
                    processor = persister.Get(sagaId);
                }

                bus.Send(bus.Endpoint, new SubmitOrderMessage { CorrelationId = sagaId });

                wait.WaitOne(TimeSpan.FromSeconds(30), false);

                while (processor != null)
                {
                    Thread.Sleep(500);
                    processor = persister.Get(sagaId);
                }

                Assert.Null(processor);
            }
        }

        [Fact]
        public void Completing_saga_with_no_persisted_state_does_not_throw()
        {
            using (var bus = container.Resolve<IStartableServiceBus>())
            {
                bus.Start();

                var storeProvider = new TestDocumentStoreProvider(container.Resolve<IDocumentStore>());
                var persister = new RavenSagaPersister<OrderProcessor>(storeProvider, container.Resolve<IServiceLocator>(), container.Resolve<IReflection>());
                Assert.DoesNotThrow(() =>
                {
                    persister.Complete(new OrderProcessor());
                });
            }
        }

        #region Nested type: AddLineItemMessage

        public class AddLineItemMessage : ISagaMessage
        {
            #region ISagaMessage Members

            public Guid CorrelationId { get; set; }

            #endregion
        }

        #endregion

        #region Nested type: NewOrderMessage

        public class NewOrderMessage : ISagaMessage
        {
            public Guid CorrelationId
            {
                get;
                set;
            }
        }

        #endregion

        #region Nested type: OrderProcessor

        public class OrderProcessor :
                ISaga<List<object>>,
                InitiatedBy<NewOrderMessage2>,
                InitiatedBy<NewOrderMessage>,
                Orchestrates<AddLineItemMessage>,
                Orchestrates<SubmitOrderMessage>
        {
            public static List<object> LastState;

            public OrderProcessor()
            {
                State = new List<object>();
            }
            #region InitiatedBy<NewOrderMessage> Members

            public void Consume(NewOrderMessage pong)
            {
                State.Add(pong);
                sagaId = Id;
                LastState = State;
                wait.Set();
            }

            public Guid Id { get; set; }
            public bool IsCompleted { get; set; }

            #endregion

            #region Orchestrates<AddLineItemMessage> Members

            public void Consume(AddLineItemMessage pong)
            {
                State.Add(pong);
                sagaId = Id;
                LastState = State;
                wait.Set();
            }

            #endregion

            public void Consume(SubmitOrderMessage message)
            {
                IsCompleted = true;
                LastState = State;
                wait.Set();
            }

            public List<object> State
            {
                get;
                set;
            }

            public void Consume(NewOrderMessage2 message)
            {
                LastState = State;
                sagaId = Id;
                wait.Set();
            }
        }

        #endregion

        #region Nested type: SubmitOrderMessage

        public class SubmitOrderMessage : ISagaMessage
        {
            #region ISagaMessage Members

            public Guid CorrelationId { get; set; }

            #endregion
        }

        #endregion

        #region Nested type: TestDocumentStoreProvider
        public class TestDocumentStoreProvider : IDocumentStoreProvider
        {
            private readonly IDocumentStore store;

            public TestDocumentStoreProvider(IDocumentStore store)
            {
                this.store = store;
            }

            public IDocumentSession Current
            {
                get { return store.OpenSession(); }
            }
        }
        #endregion
    }

    public class NewOrderMessage2
    {
    }
}