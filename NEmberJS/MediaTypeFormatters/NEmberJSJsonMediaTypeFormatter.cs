﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NEmberJS.Converters;

namespace NEmberJS.MediaTypeFormatters
{
    public class NEmberJSJsonMediaTypeFormatter : JsonMediaTypeFormatter
    {
        private readonly ConcurrentDictionary<Type, Type> _envelopeTypeCache =
            new ConcurrentDictionary<Type, Type>();

        private readonly ConcurrentDictionary<Type, bool> _shouldEnvelopeCache =
            new ConcurrentDictionary<Type, bool>();

        private readonly NEmberJSJsonConverter _nEmberJsConverter;

        public NEmberJSJsonMediaTypeFormatter(IPluralizer pluralizer)
        {
            _nEmberJsConverter = new NEmberJSJsonConverter(pluralizer);

            SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            SerializerSettings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
            SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
            
            //SerializerSettings.Converters.Add(new StringEnumConverter { CamelCaseText = true });
            SerializerSettings.Converters.Add(new WhiteSpaceTrimStringConverter());

            SerializerSettings.Converters.Add(_nEmberJsConverter);
        }

        public void AddMetaProvider(IMetaProvider provider)
        {
            _nEmberJsConverter.AddMetaProvider(provider);
        }

        public override Task WriteToStreamAsync(
            Type type,
            object value,
            Stream writeStream,
            HttpContent content,
            TransportContext transportContext)
        {
            // the object value is a more reliable source for the type, but if it's not available 
            // (because the controller returns null for example), fallback to using the type the 
            // formatter thinks it should be formatting...
            var serializedType = value != null
                ? value.GetType()
                : type;

            var shouldEnvelope = _shouldEnvelopeCache.GetOrAdd(serializedType, ShouldEnvelope);

            var innerValue = shouldEnvelope
                ? new EnvelopeWrite(value)
                : value;
            
            return base.WriteToStreamAsync(type, innerValue, writeStream, content, transportContext);
        }

        public override Task<object> ReadFromStreamAsync(
            Type type,
            Stream readStream,
            HttpContent content,
            IFormatterLogger formatterLogger)
        {
            var shouldEnvelope = _shouldEnvelopeCache.GetOrAdd(type, ShouldEnvelope);

            var innerType = shouldEnvelope
                ? _envelopeTypeCache.GetOrAdd(type, t => typeof(EnvelopeRead<>).MakeGenericType(t))
                : type;

            return base.ReadFromStreamAsync(innerType, readStream, content, formatterLogger);
        }

        public bool ShouldEnvelope(Type type)
        {
            if (type == typeof(object))
            {
                return false;
            }

            if (type == typeof(IEnumerable))
            {
                return false;
            }
            var innerType = GetInnerType(type);

            if (innerType == typeof(string))
            {
                return false;
            }

            if (innerType == typeof(DateTime))
            {
                return false;
            }

            if (innerType == typeof(decimal))
            {
                return false;
            }

            if (innerType.IsPrimitive)
            {
                return false;
            }

            if (IsAnonymousType(innerType))
            {
                return false;
            }


            return true;
        }

        private Type GetInnerType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                return underlying;
            }

            if (type.IsGenericType
                && typeof(IEnumerable<>).IsAssignableFrom(type.GetGenericTypeDefinition()))
            {
                return type.GetGenericArguments()[0];
            }

            return type;
        }

        private static bool IsAnonymousType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            return Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), false)
                   && type.IsGenericType && type.Name.Contains("AnonymousType")
                   && (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
                   && (type.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic;
        }

        private static bool IsSideLoad(Type type)
        {
            return type.CustomAttributes.Any(x => x.AttributeType == typeof (SideloadAttribute));
        }
    }
}
