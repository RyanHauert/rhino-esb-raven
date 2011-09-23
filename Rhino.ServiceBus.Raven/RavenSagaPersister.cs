using System;
using System.Globalization;
using System.Reflection;
using Raven.Client;
using Rhino.ServiceBus.Internal;

namespace Rhino.ServiceBus.Raven
{
    public class RavenSagaPersister<TSaga> : ISagaPersister<TSaga> where TSaga : class, IAccessibleSaga
    {
        private readonly IDocumentStoreProvider store;
        private readonly IServiceLocator serviceLocator;
        private readonly IReflection reflection;

        private readonly MethodInfo loadMethod;
        private readonly Type persistedStateType;

        public RavenSagaPersister(IDocumentStoreProvider store, IServiceLocator serviceLocator, IReflection reflection)
        {
            this.store = store;
            this.serviceLocator = serviceLocator;
            this.reflection = reflection;

            var stateProperty = typeof(TSaga).GetProperty("State");
            persistedStateType = typeof(PersistedSagaState<>).MakeGenericType(stateProperty.PropertyType);
            var method = typeof(IDocumentSession).GetMethod("Load", new[] { typeof(string) });
            loadMethod = method.MakeGenericMethod(persistedStateType);
        }

        public TSaga Get(Guid id)
        {
            var state = LoadPersistedState(id);
            if (state == null)
                return null;
            var saga = serviceLocator.Resolve<TSaga>();
            saga.Id = id;
            reflection.Set(saga, "State", type => reflection.Get(state, "State"));
            return saga;
        }

        public void Save(TSaga saga)
        {
            var persistedState = LoadPersistedState(saga.Id) ?? CreateDefault(persistedStateType, saga.Id);
            var state = reflection.Get(saga, "State");
            reflection.Set(persistedState, "State", type => state);
            store.Current.Store(persistedState);
        }

        public void Complete(TSaga saga)
        {
            object persistedState = LoadPersistedState(saga.Id);
            if (persistedState != null)
            {
                store.Current.Delete(persistedState); 
            }
        }

        private object LoadPersistedState(Guid id)
        {
            return loadMethod.Invoke(store.Current, new[] { CreateDocumentId(id) });
        }

        private static object CreateDefault(Type persistedType, Guid id)
        {
            var persistedState = (IPersistedSagaIdentifier)Activator.CreateInstance(persistedType);
            persistedState.Id = CreateDocumentId(id);
            return persistedState;
        }

        private static string CreateDocumentId(Guid id)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0}/{1}", typeof(TSaga).Name, id);
        }
    }
}