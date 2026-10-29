using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using InduLink.Exceptions;
using TwinCAT.TypeSystem;

namespace InduLink.Protocols.Ads
{
    /// <summary>根据当前 PLC 元数据校验一维数组，独立于连接和数据传输。</summary>
    internal static class AdsArrayTypeValidator
    {
        public static int GetLength(string address, IDataType dataType, Type elementType, int? count = null)
        {
            var resolved = ResolveAlias(dataType);
            var supplied = count.HasValue ? "实际传入 " + count + " 个 " + elementType.Name + " 元素" : "请求读取 " + elementType.Name + "[]";
            if (!(resolved is IArrayType array))
                throw new InduLinkDataConversionException("ADS '" + address + "' 的 PLC 类型是 " + (dataType?.Name ?? "未知") + "，需要一维数组；" + supplied + "。");
            if (array.Dimensions.Count != 1 || array.IsJagged)
                throw new InduLinkDataConversionException("ADS '" + address + "' 的 PLC 类型是 " + array.Name + "，只支持完整的一维数组；" + supplied + "。");

            var dimension = array.Dimensions[0];
            var length = dimension.ElementCount;
            var upperBound = (long)dimension.LowerBound + length - 1;
            var declaration = "ARRAY[" + dimension.LowerBound + ".." + upperBound + "] OF " + array.ElementTypeName;
            var context = "ADS '" + address + "' 的 PLC 声明为 " + declaration + "，需要 " + length + " 个元素；" + supplied + "。";
            if (length <= 0 || (count.HasValue && count.Value != length))
                throw new InduLinkDataConversionException(context);

            var plcElement = ResolveAlias(array.ElementType);
            if (plcElement is IStringType)
                throw new InduLinkDataConversionException(context + " 字符串数组需要显式长度和编码布局，请使用 ReadAnyAsync/WriteAnyAsync。");
            if (plcElement?.Category == DataTypeCategory.Struct || plcElement?.Category == DataTypeCategory.Union)
            {
                // 大小相同不代表字段布局相同；字段顺序、Pack 和 MarshalAs 仍由调用方声明。
                if (elementType.IsPrimitive || elementType.IsEnum || (!elementType.IsLayoutSequential && !elementType.IsExplicitLayout))
                    throw new InduLinkDataConversionException(context + " 结构体元素需要 Sequential 或 Explicit 布局。");
                int size;
                try { size = Marshal.SizeOf(elementType); }
                catch (ArgumentException ex) { throw new InduLinkDataConversionException(context + " C# 元素不能按固定布局封送。", ex); }
                if (size != plcElement.ByteSize)
                    throw new InduLinkDataConversionException(context + " PLC 元素占 " + plcElement.ByteSize + " 字节，C# 元素占 " + size + " 字节。");
                return length;
            }

            // 按语义类型核对，不能让同为四字节的 DINT/REAL 或 BOOL/整数混用。
            var expectedType = (plcElement as IManagedMappableType)?.ManagedType;
            var actualType = elementType.IsEnum ? Enum.GetUnderlyingType(elementType) : elementType;
            if (expectedType == null || actualType != expectedType)
                throw new InduLinkDataConversionException(context + " PLC 元素对应 " + (expectedType?.Name ?? "未确定的 CLR 类型") + "，请检查类型或使用 ReadAnyAsync/WriteAnyAsync 显式映射。");
            return length;
        }

        private static IDataType ResolveAlias(IDataType dataType)
        {
            var visited = new HashSet<IDataType>();
            // 普通别名沿基础类型解析；枚举的 ManagedType 已经指向其基础 CLR 类型。
            while (dataType is IAliasType alias && !(dataType is IEnumType))
            {
                if (!visited.Add(dataType)) return null;
                dataType = alias.BaseType;
            }
            return dataType;
        }
    }
}
