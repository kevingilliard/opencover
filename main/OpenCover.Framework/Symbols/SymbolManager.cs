﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using OpenCover.Framework.Model;
using File = OpenCover.Framework.Model.File;

namespace OpenCover.Framework.Symbols
{
    /// <summary>
    /// Interacts with the symbol PDB files and uses Model based entities
    /// </summary>
    public class SymbolManager : ISymbolManager, IDisposable
    {
        private readonly ICommandLine _commandLine;
        private readonly ISymbolReaderFactory _symbolReaderFactory;
        private string _modulePath;
        private ISymbolReader _symbolReader;
        private Assembly _assembly;
 
        public SymbolManager(ICommandLine commandLine, ISymbolReaderFactory symbolReaderFactory)
        {
            _commandLine = commandLine;
            _symbolReaderFactory = symbolReaderFactory;
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += CurrentDomain_ReflectionOnlyAssemblyResolve;
        }

        public void Initialise(string modulePath)
        {
            _modulePath = modulePath;
            _assembly = Assembly.ReflectionOnlyLoadFrom(_modulePath);
            _symbolReader = _symbolReaderFactory.GetSymbolReader(modulePath, _commandLine.TargetDir);
        }

        public string ModulePath
        {
            get { return _modulePath; }
        }

        static Assembly CurrentDomain_ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
        {
            return Assembly.ReflectionOnlyLoad(args.Name);
        }

        /// <summary>
        /// Get a list of files that the module was built with
        /// </summary>
        /// <returns></returns>
        public File[] GetFiles()
        {
            var docs = _symbolReader.GetDocuments();
            return docs
                .Where(doc => !string.IsNullOrWhiteSpace(doc.URL))
                .Select(x=>new File(){FullPath = x.URL})
                .ToArray();
        }

        /// <summary>
        /// Get a list of types that it should be able to instrument i.e. can have methods
        /// </summary>
        /// <returns></returns>
        public Class[] GetInstrumentableTypes()
        {
            // for now just classes but structs can have methods too
            var types = _assembly
                .GetTypes()
                .Where(EvaluateType)
                .Select(x => new Class(){FullName = x.FullName})
                .ToArray();

            return types;
        }

        private static bool EvaluateType(Type type)
        {
            if (!type.IsClass) return false;
            return !type.GetCustomAttributesData()
               .Any(x => x.Constructor.DeclaringType == typeof(CompilerGeneratedAttribute));
        }

        /// <summary>
        /// Get a list of constructors for the type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <remarks>
        /// The only constructors needed those that are declared in that class scope i.e. not base class methods
        /// However every class has at least 1 constructor
        /// </remarks>
        public Method[] GetConstructorsForType(Class type)
        {
            return _assembly.GetType(type.FullName).GetConstructors(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly |
                BindingFlags.NonPublic).Where(EvaluateConstructorInfo)
                .Select(x => new Method{Name = x.Name, MetadataToken = x.MetadataToken})
                .ToArray();
        }

        private static bool EvaluateConstructorInfo(ConstructorInfo constructorInfo)
        {
            //Debug.WriteLine("{0}({1})", constructorInfo.Name, constructorInfo.MetadataToken);
            return true;
        }

        /// <summary>
        /// Get a list of methods fro the type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <remarks>
        /// The only methods needed those that are declared in that class scope i.e. not base class methods
        /// </remarks>
        public Method[] GetMethodsForType(Class type)
        {
            return _assembly.GetType(type.FullName).GetMethods(
                BindingFlags.Instance | 
                BindingFlags.Public | 
                BindingFlags.Static |
                BindingFlags.DeclaredOnly |
                BindingFlags.NonPublic).Where(EvaluateMethodInfo)
                .Select(x => new Method { Name = x.Name, MetadataToken = x.MetadataToken })
                .ToArray();
        }

        private static bool EvaluateMethodInfo(MethodInfo methodInfo)
        {
            // do not show auto properties as they have no sequence points, 
            // but strangely they are JITted - which only happens if they are going to be used
            // so we could possibly use this info for coverage even if not for visit counts 
            // (enter/exit/tailcall perhaps?)
            //Debug.WriteLine("{0}({1})", methodInfo.Name, methodInfo.MetadataToken);
            return !methodInfo.GetCustomAttributesData()
                .Any(x => x.Constructor.DeclaringType == typeof(CompilerGeneratedAttribute));
        }

        /// <summary>
        /// Get a list of sequence points for a supplied token
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public SequencePoint[] GetSequencePointsForToken(int token)
        {
            try
            {
                var symbolToken = new SymbolToken(token);
                var method = _symbolReader.GetMethod(symbolToken);
                var count = method.SequencePointCount;

                var offsets = new int[count];
                var sls = new int[count];
                var scs = new int[count];
                var els = new int[count];
                var ecs = new int[count];
                var docs = new ISymbolDocument[count];

                method.GetSequencePoints(offsets, docs, sls, scs, els, ecs);

                var list = new List<SequencePoint>();
                for (UInt32 i = 0; i < count; i++)
                {
                    list.Add(new SequencePoint()
                    {
                        Ordinal = i,
                        Offset = offsets[i],
                        StartLine = sls[i],
                        StartColumn = scs[i],
                        EndLine = els[i],
                        EndColumn = ecs[i]
                    });
                }
                return list.ToArray();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= CurrentDomain_ReflectionOnlyAssemblyResolve;
        }
    }
}