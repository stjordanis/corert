// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Internal.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace System.Runtime.InteropServices
{
    [Flags]
    public enum McgInterfaceFlags : byte
    {
        None = 0x00,
        isIInspectable = 0x01,    // this interface derives from IInspectable
        isDelegate = 0x02,    // this entry is for a WinRT delegate type, ItfType is the type of a managed delegate type
        isInternal = 0x04,

        useSharedCCW = 0x08,  // this entry uses shared ccwVTable + thunk function
        SharedCCWMask = 0xF8,

        useSharedCCW_IVector = 0x08,  // 4-bit shared ccw index: 16 max, 8 used
        useSharedCCW_IVectorView = 0x18,
        useSharedCCW_IIterable = 0x28,
        useSharedCCW_IIterator = 0x38,
        useSharedCCW_AsyncOperationCompletedHandler = 0x48,
        useSharedCCW_IVectorBlittable = 0x58,
        useSharedCCW_IVectorViewBlittable = 0x68,
        useSharedCCW_IIteratorBlittable = 0x78,
    }

    [Flags]
    public enum McgClassFlags : int
    {
        None = 0,

        /// <summary>
        /// This represents the types MarshalingBehavior.
        /// </summary>
        MarshalingBehavior_Inhibit = 1,
        MarshalingBehavior_Free = 2,
        MarshalingBehavior_Standard = 3,

        MarshalingBehavior_Mask = 3,

        /// <summary>
        /// GCPressureRange
        /// </summary>
        GCPressureRange_WinRT_Default = 1 << 2,
        GCPressureRange_WinRT_Low = 2 << 2,
        GCPressureRange_WinRT_Medium = 3 << 2,
        GCPressureRange_WinRT_High = 4 << 2,
        GCPressureRange_Mask = 7 << 2,

        /// <summary>
        /// Either a WinRT value type, or a projected class type
        /// In either case, it is not a __ComObject and we can't create it using CreateComObject
        /// </summary>
        NotComObject = 32,

        /// <summary>
        /// This type is sealed
        /// </summary>
        IsSealed = 64,

        /// <summary>
        /// This type is a WinRT type and we'll return Kind=Metadata in type name marshalling
        /// </summary>
        IsWinRT = 128
    }

    /// <summary>
    /// Per-native-interface information generated by MCG
    /// </summary>
    [CLSCompliant(false)]
    public struct McgInterfaceData                          // 36 bytes on 32-bit platforms
    {
        /// <summary>
        /// NOTE: Managed debugger depends on field name: "FixupItfType" and field type must be FixupRuntimeTypeHandle
        /// Update managed debugger whenever field name/field type is changed.
        /// See CordbObjectValue::WalkPtrAndTypeData in debug\dbi\values.cpp
        /// </summary>
        public FixupRuntimeTypeHandle FixupItfType;                   //  1 pointer

        // Optional fields(FixupDispatchClassType and FixupDynamicAdapterClassType)
        public FixupRuntimeTypeHandle FixupDispatchClassType;         //  1 pointer, around 80% usage
        public FixupRuntimeTypeHandle FixupDynamicAdapterClassType;   //  1 pointer, around 20% usage
        public RuntimeTypeHandle ItfType
        {
            get
            {
                return FixupItfType.RuntimeTypeHandle;
            }
            set
            {
                FixupItfType = new FixupRuntimeTypeHandle(value);
            }
        }
        public RuntimeTypeHandle DispatchClassType
        {
            get
            {
                return FixupDispatchClassType.RuntimeTypeHandle;
            }
        }
        public RuntimeTypeHandle DynamicAdapterClassType
        {
            get
            {
                return FixupDynamicAdapterClassType.RuntimeTypeHandle;
            }
        }
        // Fixed fields
        public Guid ItfGuid;                   // 16 bytes

        /// <summary>
        /// NOTE: Managed debugger depends on field name: "Flags" and field type must be an enum type
        /// Update managed debugger whenever field name/field type is changed.
        /// See CordbObjectValue::WalkPtrAndTypeData in debug\dbi\values.cpp
        /// </summary>
        public McgInterfaceFlags Flags;                     //  1 byte

        /// <summary>
        /// Whether this type is a IInspectable type
        /// </summary>
        internal bool IsIInspectable
        {
            get
            {
                return (Flags & McgInterfaceFlags.isIInspectable) != McgInterfaceFlags.None;
            }
        }

        internal bool IsIInspectableOrDelegate
        {
            get
            {
                return (Flags & (McgInterfaceFlags.isIInspectable | McgInterfaceFlags.isDelegate)) != McgInterfaceFlags.None;
            }
        }

        public short MarshalIndex;              //  2 bytes: Index into InterfaceMarshalData array for shared CCW, also used for internal module sequential type index
        //  Optional fields(CcwVtable)
        //  TODO fyuan define larger McgInterfaceData for merging interop code (shared CCW, default eventhandler)
        public IntPtr CcwVtable;                 //  1 pointer, around 20-40% usage
    }

    [CLSCompliant(false)]
    public struct McgGenericArgumentMarshalInfo             // Marshal information for generic argument T
    {
        // sizeOf(T)
        public uint ElementSize;
        // Class Type Handle for sealed T winrt class
        public FixupRuntimeTypeHandle FixupElementClassType;
        // Interface Type Handle for T interface type 
        public FixupRuntimeTypeHandle FixupElementInterfaceType;
        // Type Handle for IAsyncOperation<T>
        public FixupRuntimeTypeHandle FixupAsyncOperationType;
        // Type Handle for Iterator<T>
        public FixupRuntimeTypeHandle FixupIteratorType;
        // Type Handle for VectorView<T>
        public FixupRuntimeTypeHandle FixupVectorViewType;
        public RuntimeTypeHandle AsyncOperationType
        {
            get
            {
                return FixupAsyncOperationType.RuntimeTypeHandle;
            }
        }
        public RuntimeTypeHandle ElementClassType
        {
            get
            {
                return FixupElementClassType.RuntimeTypeHandle;
            }
        }
        public RuntimeTypeHandle ElementInterfaceType
        {
            get
            {
                return FixupElementInterfaceType.RuntimeTypeHandle;
            }
        }

        public RuntimeTypeHandle IteratorType
        {
            get
            {
                return FixupIteratorType.RuntimeTypeHandle;
            }
        }

        public RuntimeTypeHandle VectorViewType
        {
            get
            {
                return FixupVectorViewType.RuntimeTypeHandle;
            }
        }
    }

    /// <summary>
    /// Per-WinRT-class information generated by MCG.  This is used for TypeName marshalling and for 
    /// CreateComObject.  For the TypeName marshalling case, we have Nullable<T> / KeyValuePair<K,V> value 
    /// classes as the ClassType field and WinRT names like Windows.Foundation.IReference`1<blah> in the name
    /// field.  These entries are filtered out by CreateComObject using the Flags field.
    /// </summary>
    [CLSCompliant(false)]
    public struct McgClassData
    {
        public FixupRuntimeTypeHandle FixupClassType;         // FixupRuntimeTypeHandle for type in CLR (projected) view
        public RuntimeTypeHandle ClassType                    // RuntimeTypeHandle of FixupRuntimeTypeHandle
        {
            get
            {
                return FixupClassType.RuntimeTypeHandle;
            }
        }

        public McgClassFlags Flags;      // Flags (whether it is a ComObject, whether it can be boxed, etc)
        internal GCPressureRange GCPressureRange
        {
            get
            {
                switch (Flags & McgClassFlags.GCPressureRange_Mask)
                {
                    case McgClassFlags.GCPressureRange_WinRT_Default:
                        return GCPressureRange.WinRT_Default;

                    case McgClassFlags.GCPressureRange_WinRT_Low:
                        return GCPressureRange.WinRT_Low;

                    case McgClassFlags.GCPressureRange_WinRT_Medium:
                        return GCPressureRange.WinRT_Medium;

                    case McgClassFlags.GCPressureRange_WinRT_High:
                        return GCPressureRange.WinRT_High;

                    default:
                        return GCPressureRange.None;
                }
            }
        }

        internal ComMarshalingType MarshalingType
        {
            get
            {
                switch (Flags & McgClassFlags.MarshalingBehavior_Mask)
                {
                    case McgClassFlags.MarshalingBehavior_Inhibit:
                        return ComMarshalingType.Inhibit;

                    case McgClassFlags.MarshalingBehavior_Free:
                        return ComMarshalingType.Free;

                    case McgClassFlags.MarshalingBehavior_Standard:
                        return ComMarshalingType.Standard;

                    default:
                        return ComMarshalingType.Unknown;
                }
            }
        }

        /// <summary>
        /// The type handle for its base class
        /// 
        /// There are two ways to access base class: RuntimeTypeHandle or Index
        /// Ideally we want to use typehandle for everything - but DR throws it away and breaks the inheritance chain
        ///  - therefore we would only use index for "same module" and type handle for "cross module"
        ///  
        ///  Code Pattern:
        ///      if (BaseClassIndex >=0) { // same module }
        ///      else if(!BaseClassType.Equals(default(RuntimeTypeHandle)) { // cross module}
        ///      else { // it doesn't have base}
        /// </summary>
        public FixupRuntimeTypeHandle FixupBaseClassType;         // FixupRuntimeTypeHandle for Base class type in CLR (projected) view
        public RuntimeTypeHandle BaseClassType                    // RuntimeTypeHandle of BaseClass
        {
            get
            {
                return FixupBaseClassType.RuntimeTypeHandle;
            }
        }
        public short BaseClassIndex;    // Index to the base class; 
        
        /// <summary>
        ///  The type handle for its default Interface
        ///  The comment above for BaseClassType applies for DefaultInterface as well
        /// </summary>
        public FixupRuntimeTypeHandle FixupDefaultInterfaceType;         // FixupRuntimeTypeHandle for DefaultInterface type in CLR (projected) view
        public RuntimeTypeHandle DefaultInterfaceType                    // RuntimeTypeHandle of DefaultInterface
        {
            get
            {
                return FixupDefaultInterfaceType.RuntimeTypeHandle;
            }
        }        
        public short DefaultInterfaceIndex;  // Index to the default interface
    }

    [CLSCompliant(false)]
    public struct McgHashcodeVerifyEntry
    {
        public FixupRuntimeTypeHandle FixupTypeHandle;
        public RuntimeTypeHandle TypeHandle
        {
            get
            {
                return FixupTypeHandle.RuntimeTypeHandle;
            }
        }
        public uint HashCode;
    }


    /// <summary>
    /// Mcg data used for boxing
    /// Boxing refers to IReference/IReferenceArray boxing, as well as projection support when marshalling
    /// IInspectable, such as IKeyValuePair, System.Uri, etc.
    /// So this supports boxing in a broader sense that it supports WinRT type <-> native type projection
    /// There are 3 cases:
    /// 1. IReference<T> / IReferenceArray<T>. It is boxed to a managed wrapper and unboxed either from the wrapper or from native IReference/IReferenceArray RCW (through unboxing stub)
    /// 2. IKeyValuePair<K, V>. Very similiar to #1 except that it does not have propertyType
    /// 3. All other cases, including System.Uri, NotifyCollectionChangedEventArgs, etc. They go through boxing stub and unboxing stub.
    ///
    /// NOTE: Even though this struct doesn't have a name in itself, there is a parallel array 
    /// m_boxingDataNameMap that holds the corresponding class names for each boxing data
    /// </summary>
    [CLSCompliant(false)]
    public struct McgBoxingData
    {
        /// <summary>
        /// The target type that triggers the boxing. Used in search
        /// </summary>
        public FixupRuntimeTypeHandle FixupManagedClassType;

        /// <summary>
        /// HIDDEN
        /// This is actually saved in m_boxingDataNameMap
        /// The runtime class name that triggers the unboxing. Used in search.
        /// </summary>
        /// public string Name;

        /// <summary>
        /// A managed wrapper for IReference/IReferenceArray/IKeyValuePair boxing
        /// We create the wrapper directly instead of going through a boxing stub. This saves us some
        /// disk space (300+ boxing stub in a typical app), but we need to see whether this trade off 
        /// makes sense long term.
        /// </summary>
        public FixupRuntimeTypeHandle FixupCLRBoxingWrapperType;
        public RuntimeTypeHandle ManagedClassType
        {
            get
            {
                return FixupManagedClassType.RuntimeTypeHandle;
            }
        }
        public RuntimeTypeHandle CLRBoxingWrapperType
        {
            get
            {
                return FixupCLRBoxingWrapperType.RuntimeTypeHandle;
            }
        }
        /// <summary>
        /// General boxing stub - boxing an managed object instance into a native object instance (RCW)
        /// This is used for special cases where managed wrappers aren't suitable, mainly for projected types
        /// such as System.Uri.
        ///   
        /// Prototype:
        /// object Boxing_Stub(object target)
        /// </summary>
        public IntPtr BoxingStub;

        /// <summary>
        /// General unboxing stub - unbox a native object instance (RCW) into a managed object (interestingly,
        /// can be boxed in the managed sense)
        /// 
        /// object UnboxingStub(object target)
        /// </summary>
        public IntPtr UnboxingStub;

        /// <summary>
        /// Corresponding PropertyType
        /// Only used when boxing into a managed wrapper - because it is only meaningful in IReference
        /// & IReferenceArray
        /// </summary>
        public short PropertyType;
    }

    /// <summary>
    /// This information is separate from the McgClassData[]
    /// as its captures the types which are not imported by the MCG.
    /// </summary>
    [CLSCompliant(false)]
    public struct McgTypeNameMarshalingData
    {
        public FixupRuntimeTypeHandle FixupClassType;
        public RuntimeTypeHandle ClassType
        {
            get
            {
                return FixupClassType.RuntimeTypeHandle;
            }
        }
    }

    public enum McgStructMarshalFlags
    {
        None,

        /// <summary>
        /// This struct has invalid layout information, most likely because it is marked LayoutKind.Auto
        /// </summary>
        HasInvalidLayout
    }

    [CLSCompliant(false)]
    public struct McgStructMarshalData
    {
        public FixupRuntimeTypeHandle FixupSafeStructType;
        public FixupRuntimeTypeHandle FixupUnsafeStructType;
        public RuntimeTypeHandle SafeStructType
        {
            get
            {
                return FixupSafeStructType.RuntimeTypeHandle;
            }
        }
        public RuntimeTypeHandle UnsafeStructType
        {
            get
            {
                return FixupUnsafeStructType.RuntimeTypeHandle;
            }
        }
        public IntPtr MarshalStub;
        public IntPtr UnmarshalStub;
        public IntPtr DestroyStructureStub;

        public McgStructMarshalFlags Flags;

        /// <summary>
        /// This struct has invalid layout information, most likely because it is marked LayoutKind.Auto
        /// We'll throw exception when this struct is getting marshalled
        /// </summary>
        internal bool HasInvalidLayout
        {
            get
            {
                return (Flags & McgStructMarshalFlags.HasInvalidLayout) != 0;
            }
        }

        public int FieldOffsetStartIndex;   // start index to its field offset data
        public int NumOfFields;             // number of fields
    }

    [CLSCompliant(false)]
    public struct McgUnsafeStructFieldOffsetData
    {
        public uint Offset; // offset value  in bytes
    }

    /// <summary>
    /// Base class for KeyValuePairImpl<T>
    /// </summary>
    public abstract class BoxedKeyValuePair : IManagedWrapper
    {
        // Called by public object McgModule.Box(object obj, int boxingIndex) after allocating instance
        public abstract object Initialize(object val);

        public abstract object GetTarget();
    }

    /// <summary>
    /// Supports unboxing managed wrappers such as ReferenceImpl / KeyValuePair
    /// </summary>
    public interface IManagedWrapper
    {
        object GetTarget();
    }

    /// <summary>
    /// Base class for ReferenceImpl<T>/ReferenceArrayImpl<T>
    /// </summary>
    public class BoxedValue : IManagedWrapper
    {
        protected object m_data;        // boxed value
        protected short m_type;        // Windows.Foundation.PropertyType
        protected bool m_unboxed;     // false if T ReferenceImpl<T>.m_value needs to be unboxed from m_data when needed

        public BoxedValue(object val, int type)
        {
            m_data = val;
            m_type = (short)type;
        }

        // Called by public object Box(object obj, int boxingIndex) after allocating instance
        // T ReferenceImpl<T>.m_value needs to be unboxed from m_data when needed
        virtual public void Initialize(object val, int type)
        {
            m_data = val;
            m_type = (short)type;
        }

        public object GetTarget()
        {
            return m_data;
        }

        public override string ToString()
        {
            if (m_data != null)
            {
                return m_data.ToString();
            }
            else
            {
                return "null";
            }
        }
    }

    /// <summary>
    /// Entries for WinRT classes that MCG didn't see in user code
    /// We need to make sure when we are marshalling these types, we need to hand out the closest match    
    /// For example, if MCG only sees DependencyObject but native is passing MatrixTransform, we need 
    /// to give user the next best thing - dependencyObject, so that user can cast it to DependencyObject
    /// 
    /// MatrixTransform -> DependencyObject
    /// </summary>
    [CLSCompliant(false)]
    public struct McgAdditionalClassData
    {
        public int ClassDataIndex;      // Pointing to the "next best" class (DependencyObject, for example)

        public FixupRuntimeTypeHandle FixupClassType;      // Pointing to the "next best" class (DependencyObject, for example)
        public RuntimeTypeHandle ClassType
        {
            get
            {
                return FixupClassType.RuntimeTypeHandle;
            }
        }
    }

    /// <summary>
    /// Maps from an ICollection or IReadOnlyCollection type to the corresponding entries in m_interfaceTypeInfo 
    /// for IList, IDictionary, IReadOnlyList, IReadOnlyDictionary
    /// </summary>
    [CLSCompliant(false)]
    public struct McgCollectionData
    {
        public FixupRuntimeTypeHandle FixupCollectionType;
        public RuntimeTypeHandle CollectionType
        {
            get
            {
                return FixupCollectionType.RuntimeTypeHandle;
            }
        }
        public FixupRuntimeTypeHandle FixupFirstType;
        public RuntimeTypeHandle FirstType
        {
            get
            {
                return FixupFirstType.RuntimeTypeHandle;
            }
        }
        public FixupRuntimeTypeHandle FixupSecondType;
        public RuntimeTypeHandle SecondType
        {
            get
            {
                return FixupSecondType.RuntimeTypeHandle;
            }
        }
    }

    /// <summary>
    /// Captures data for each P/invoke delegate type we decide to import
    /// </summary>
    [CLSCompliant(false)]
    public struct McgPInvokeDelegateData
    {
        /// <summary>
        /// Type of the delegate
        /// </summary>
        public FixupRuntimeTypeHandle FixupDelegate;
        public RuntimeTypeHandle Delegate
        {
            get
            {
                return FixupDelegate.RuntimeTypeHandle;
            }
        }
        /// <summary>
        /// The stub called from thunk that does the marshalling when calling managed delegate (as a function
        /// pointer) from native code
        /// </summary>
        public IntPtr ReverseStub;

        /// <summary>
        /// The stub called from thunk that does the marshalling when calling managed open static delegate (as a function
        /// pointer) from native code
        /// </summary>
        public IntPtr ReverseOpenStaticDelegateStub;

        /// <summary>
        /// This creates a delegate wrapper class that wraps the native function pointer and allows managed
        /// code to call it
        /// </summary>
        public IntPtr ForwardDelegateCreationStub;
    }


    /// <summary>
    /// Base class for all 'wrapper' classes that wraps a native function pointer
    /// The forward delegates (that wraps native function pointers) points to derived Invoke method of this
    /// class, and the Invoke method would implement the marshalling and making the call
    /// </summary>
    public abstract class NativeFunctionPointerWrapper
    {
        public NativeFunctionPointerWrapper(IntPtr nativeFunctionPointer)
        {
            m_nativeFunctionPointer = nativeFunctionPointer;
        }

        IntPtr m_nativeFunctionPointer;

        public IntPtr NativeFunctionPointer
        {
            get { return m_nativeFunctionPointer; }
        }
    }

    [CLSCompliant(false)]
    public struct McgCCWFactoryInfoEntry
    {
        public FixupRuntimeTypeHandle FixupFactoryType;
        public RuntimeTypeHandle FactoryType
        {
            get
            {
                return FixupFactoryType.RuntimeTypeHandle;
            }
        }
    }

    /// <summary>
    /// Static per-type CCW information
    /// </summary>
    [CLSCompliant(false)]
    public struct CCWTemplateData
    {
        /// <summary>
        /// RuntimeTypeHandle of the class that this CCWTemplateData is for
        /// </summary>
        public FixupRuntimeTypeHandle FixupClassType;
        public RuntimeTypeHandle ClassType
        {
            get
            {
                return FixupClassType.RuntimeTypeHandle;
            }
        }

        /// <summary>
        /// The type handle for its base class (that is also a managed type)
        /// 
        /// There are two ways to access base class: RuntimeTypeHandle or Index
        /// Ideally we want to use typehandle for everything - but DR throws it away and breaks the inheritance chain
        ///  - therefore we would only use index for "same module" and type handle for "cross module"
        ///  
        ///  Code Pattern:
        ///      if (ParentCCWTemplateIndex >=0) { // same module }
        ///      else if(!BaseClassType.Equals(default(RuntimeTypeHandle)) { // cross module}
        ///      else { // it doesn't have base}
        /// </summary>
        public FixupRuntimeTypeHandle FixupBaseType;
        public RuntimeTypeHandle BaseType
        {
            get
            {
                return FixupBaseType.RuntimeTypeHandle;
            }
        }

        /// <summary>
        /// The index of the CCWTemplateData for its base class (that is also a managed type)
        /// < 0 if does not exist or there are multiple module involved(use its BaseType property instead)
        /// </summary>
        public int ParentCCWTemplateIndex;

        /// <summary>
        /// The beginning index of list of supported interface
        /// NOTE: The list for this specific type only, excluding base classes
        /// </summary>
        public int SupportedInterfaceListBeginIndex;

        /// <summary>
        /// The total number of supported interface
        /// NOTE: The list for this specific type only, excluding base classes
        /// </summary>
        public int NumberOfSupportedInterface;

        /// <summary>
        /// Whether this CCWTemplateData belongs to a WinRT type
        /// Typically this only happens when we import a managed class that implements a WinRT type and 
        /// we'll import the base WinRT type as CCW template too, as a way to capture interfaces in the class
        /// hierarchy and also know which are the ones implemented by managed class
        /// </summary>
        public bool IsWinRTType;
    }

    /// <summary>
    /// Extra type/marshalling information for a given type used in MCG.  You can think this as a more 
    /// complete version of System.Type.
    /// </summary>
    internal unsafe class McgInterfaceInfo
    {
        int m_TypeIndex;
        int m_ModuleIndex;

        internal McgInterfaceData InterfaceData
        {
            get
            {
                return McgModuleManager.GetInterfaceDataByIndex(m_ModuleIndex, m_TypeIndex);
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public McgInterfaceInfo(int moduleIndex, int typeIndex)
        {
            m_TypeIndex = typeIndex;
            m_ModuleIndex = moduleIndex;
        }

        /// <summary>
        /// The guid of this type
        /// </summary>
        public Guid ItfGuid
        {
            get
            {
                return InterfaceData.ItfGuid;
            }
        }

        public RuntimeTypeHandle ItfType
        {
            get
            {
                return InterfaceData.ItfType;
            }
        }

        public McgInterfaceFlags Flags
        {
            get
            {
                return InterfaceData.Flags;
            }
        }

        public short MarshalIndex
        {
            get
            {
                return InterfaceData.MarshalIndex;
            }
        }

        static IntPtr[] SharedCCWList = new IntPtr[] {
#if ENABLE_WINRT
            SharedCcw_IVector.GetVtable(),
            SharedCcw_IVectorView.GetVtable(),
            SharedCcw_IIterable.GetVtable(),
            SharedCcw_IIterator.GetVtable(),

#if RHTESTCL || CORECLR
            default(IntPtr),
#else
            SharedCcw_AsyncOperationCompletedHandler.GetVtable(),
#endif
            SharedCcw_IVector_Blittable.GetVtable(),
            SharedCcw_IVectorView_Blittable.GetVtable(),

#if RHTESTCL || CORECLR
            default(IntPtr)
#else
            SharedCcw_IIterator_Blittable.GetVtable()
#endif
#endif //ENABLE_WINRT
        };

        internal unsafe IntPtr CcwVtable
        {
            get
            {
                McgInterfaceFlags flag = InterfaceData.Flags & McgInterfaceFlags.SharedCCWMask;

                if (flag != 0)
                {
                    return SharedCCWList[(int)flag >> 4];
                }

                if (InterfaceData.CcwVtable == IntPtr.Zero)
                    return IntPtr.Zero;
                    
                return CalliIntrinsics.Call__GetCcwVtable(InterfaceData.CcwVtable);
            }
        }

        /// <summary>
        /// Returns the corresponding interface type for this type
        /// </summary>
        internal RuntimeTypeHandle InterfaceType
        {
            get
            {
                return InterfaceData.ItfType;
            }
        }

        /// <summary>
        /// Returns the corresponding dispatch class type for this type
        /// </summary>
        internal RuntimeTypeHandle DispatchClassType
        {
            get
            {
                return InterfaceData.DispatchClassType;
            }
        }

        internal RuntimeTypeHandle DynamicAdapterClassType
        {
            get
            {
                return InterfaceData.DynamicAdapterClassType;
            }
        }

        internal bool HasDynamicAdapterClass
        {
            get { return !DynamicAdapterClassType.IsNull(); }
        }
    }

    /// <summary>
    /// Extra information for all the class types encoded by MCG
    /// </summary>
    internal unsafe class McgClassInfo
    {
        int m_ClassDataIndex;
        int m_ModuleIndex;

        public McgClassInfo(int moduleIndex, int classDataIndex)
        {
            m_ClassDataIndex = classDataIndex;
            m_ModuleIndex = moduleIndex;
        }

        private McgClassData ClassData
        {
            get
            {
                return McgModuleManager.GetClassDataByIndex(m_ModuleIndex, m_ClassDataIndex);
            }
        }

        internal RuntimeTypeHandle ClassType
        {
            get
            {
                return ClassData.ClassType;
            }
        }

        internal RuntimeTypeHandle BaseClassType
        {
            get
            {
                int baseClassIndex = ClassData.BaseClassIndex;
                if (baseClassIndex >= 0)
                {
                    return McgModuleManager.GetClassDataByIndex(m_ModuleIndex, baseClassIndex).ClassType;
                }
                else if (!ClassData.BaseClassType.Equals(default(RuntimeTypeHandle)))
                {
                    return ClassData.BaseClassType;
                }
                // doesn't have base class
                return default(RuntimeTypeHandle);
            }
        }

        internal RuntimeTypeHandle DefaultInterface
        {
            get
            {
                int defaultInterfaceIndex = ClassData.DefaultInterfaceIndex;
                if (defaultInterfaceIndex >= 0)
                {
                    return McgModuleManager.GetInterfaceDataByIndex(m_ModuleIndex, defaultInterfaceIndex).ItfType;
                }
                else
                {
                    return ClassData.DefaultInterfaceType;
                }
            }
        }

        internal bool Equals(McgClassInfo classInfo)
        {
            if (classInfo == null)
                return false;

            return m_ClassDataIndex == classInfo.m_ClassDataIndex && m_ModuleIndex == classInfo.m_ModuleIndex;
        }

        internal bool IsSealed
        {
            get
            {
                return ((ClassData.Flags & McgClassFlags.IsSealed) != 0);
            }
        }

        internal bool IsWinRT
        {
            get
            {
                return ((ClassData.Flags & McgClassFlags.IsWinRT) != 0);
            }
        }

        internal ComMarshalingType MarshalingType
        {
            get
            {
                return ClassData.MarshalingType;
            }
        }

        internal GCPressureRange GCPressureRange
        {
            get
            {
                return ClassData.GCPressureRange;
            }
        }
    }

    internal unsafe class CCWTemplateInfo
    {
        int m_TypeIndex;
        int m_ModuleIndex;

        /// <summary>
        /// Constructor
        /// </summary>
        public CCWTemplateInfo(int moduleIndex, int typeIndex)
        {
            m_ModuleIndex = moduleIndex;
            m_TypeIndex = typeIndex;
        }

        private CCWTemplateData CCWTemplate
        {
            get
            {
                return McgModuleManager.GetCCWTemplateDataByIndex(m_ModuleIndex, m_TypeIndex);
            }
        }

        public bool IsWinRTType
        {
            get
            {
                return CCWTemplate.IsWinRTType;
            }
        }

        public IEnumerable<RuntimeTypeHandle> ImplementedInterfaces
        {
            get
            {
                return McgModuleManager.GetImplementedInterfacesByIndex(m_ModuleIndex, m_TypeIndex);
            }
        }

        public RuntimeTypeHandle BaseClass
        {
            get
            {
                int parentCCWTemplateIndex = CCWTemplate.ParentCCWTemplateIndex;
                if (parentCCWTemplateIndex >= 0)
                {
                    return McgModuleManager.GetCCWTemplateDataByIndex(m_ModuleIndex, parentCCWTemplateIndex).ClassType;
                }
                else if (!CCWTemplate.BaseType.Equals(default(RuntimeTypeHandle)))
                {
                    return CCWTemplate.BaseType;
                }
                // doesn't have base class
                return default(RuntimeTypeHandle);
            }
        }
    }
}
