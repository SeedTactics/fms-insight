using System;
using System.Collections.Generic;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using System.Reflection;
using System.Reflection.Emit;

namespace BlackMaple.MachineWatch
{
	public class RemoteSingletons
	{
		public class WrapperBase : MarshalByRefObject
		{
			internal protected object src = null;
			
			public override object InitializeLifetimeService()
			{
				ILease lease = (ILease)base.InitializeLifetimeService();
				if (lease.CurrentState == LeaseState.Initial) {
					lease.InitialLeaseTime = TimeSpan.Zero;
				}
				return null;
			}
			
			public object LoadObject()
			{
				if (src == null)
					throw new NotImplementedException();
				return src;
			}
		}
		
		private static Type BuildWrapperType(ModuleBuilder module, Type iFace)
		{
			var proxy = module.DefineType("Wrap" + iFace.Name + DateTime.UtcNow.Ticks.ToString(),
			                              TypeAttributes.NotPublic | TypeAttributes.Sealed,
			                              typeof(WrapperBase),
			                              new Type[] {iFace});
			var loadFunc = typeof(WrapperBase).GetMethod("LoadObject");
			
			foreach (MethodInfo method in iFace.GetMethods()) {
				var parameters = method.GetParameters();
				var paramTypes = new Type[parameters.Length];
				for (int i = 0; i < parameters.Length; i++)
				     paramTypes[i] = parameters[i].ParameterType;
				
				var methodBuilder = proxy.DefineMethod(method.Name,
				                                       MethodAttributes.Public | MethodAttributes.Virtual,
				                                       method.ReturnType,
				                                       paramTypes);
				
				var ilGen = methodBuilder.GetILGenerator();
				ilGen.Emit(OpCodes.Ldarg_0);
				ilGen.Emit(OpCodes.Call, loadFunc);
				for (int i = 1; i < parameters.Length + 1; i++)
					ilGen.Emit(OpCodes.Ldarg, i);
				if (method.IsVirtual)
					ilGen.Emit(OpCodes.Callvirt, method);
				else
					ilGen.Emit(OpCodes.Call, method);
				ilGen.Emit(OpCodes.Ret);
				
				proxy.DefineMethodOverride(methodBuilder, method);
			}
			
			return proxy.CreateType();
		}
				
		public RemoteSingletons()
		{
			var domain = System.Threading.Thread.GetDomain();
			var builder = domain.DefineDynamicAssembly(new AssemblyName("Wrappers"),
			                                           AssemblyBuilderAccess.Run);
			module = builder.DefineDynamicModule("WrapperModule", false);
			marshaledObjects = new List<WrapperBase>();
		}
		
		public void RemoteSingleton(Type iFace, string uri, object obj)
		{
			var newType = BuildWrapperType(module, iFace);
			WrapperBase newObj = (WrapperBase)Activator.CreateInstance(newType);
			newObj.src = obj;
			
			RemotingServices.Marshal(newObj, uri, iFace);
			
			marshaledObjects.Add(newObj);
		}
		
		public void Disconnect()
		{
			foreach (var obj in marshaledObjects)
				RemotingServices.Disconnect(obj);
			marshaledObjects.Clear();
		}
		
		private ModuleBuilder module;
		private IList<WrapperBase> marshaledObjects;
	}
}

