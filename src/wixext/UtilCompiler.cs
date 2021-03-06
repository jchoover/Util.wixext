// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace WixToolset.Util
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;
    using WixToolset.Data;
    using WixToolset.Extensibility;
    using WixToolset.Extensibility.Data;

    /// <summary>
    /// The compiler for the WiX Toolset Utility Extension.
    /// </summary>
    public sealed class UtilCompiler : BaseCompilerExtension
    {
        // user creation attributes definitions (from sca.h)
        internal const int UserDontExpirePasswrd = 0x00000001;
        internal const int UserPasswdCantChange = 0x00000002;
        internal const int UserPasswdChangeReqdOnLogin = 0x00000004;
        internal const int UserDisableAccount = 0x00000008;
        internal const int UserFailIfExists = 0x00000010;
        internal const int UserUpdateIfExists = 0x00000020;
        internal const int UserLogonAsService = 0x00000040;
        internal const int UserLogonAsBatchJob = 0x00000080;

        internal const int UserDontRemoveOnUninstall = 0x00000100;
        internal const int UserDontCreateUser = 0x00000200;
        internal const int UserNonVital = 0x00000400;

        [Flags]
        internal enum WixFileSearchAttributes
        {
            Default = 0x001,
            MinVersionInclusive = 0x002,
            MaxVersionInclusive = 0x004,
            MinSizeInclusive = 0x008,
            MaxSizeInclusive = 0x010,
            MinDateInclusive = 0x020,
            MaxDateInclusive = 0x040,
            WantVersion = 0x080,
            WantExists = 0x100,
            IsDirectory = 0x200,
        }

        internal enum WixRegistrySearchFormat
        {
            Raw,
            Compatible,
        }

        [Flags]
        internal enum WixRegistrySearchAttributes
        {
            Raw = 0x01,
            Compatible = 0x02,
            ExpandEnvironmentVariables = 0x04,
            WantValue = 0x08,
            WantExists = 0x10,
            Win64 = 0x20,
        }

        internal enum WixComponentSearchAttributes
        {
            KeyPath = 0x1,
            State = 0x2,
            WantDirectory = 0x4,
        }

        [Flags]
        internal enum WixProductSearchAttributes
        {
            Version = 0x01,
            Language = 0x02,
            State = 0x04,
            Assignment = 0x08,
            UpgradeCode = 0x10,
        }

        internal enum WixRestartResourceAttributes
        {
            Filename = 1,
            ProcessName,
            ServiceName,
            TypeMask = 0xf,
        }

        internal enum WixRemoveFolderExOn
        {
            Install = 1,
            Uninstall = 2,
            Both = 3,
        }

        private static readonly Regex FindPropertyBrackets = new Regex(@"\[(?!\\|\])|(?<!\[\\\]|\[\\|\\\[)\]", RegexOptions.ExplicitCapture | RegexOptions.Compiled);

        public override XNamespace Namespace => "http://wixtoolset.org/schemas/v4/wxs/util";

        /// <summary>
        /// Types of Internet shortcuts.
        /// </summary>
        public enum InternetShortcutType
        {
            /// <summary>Create a .lnk file.</summary>
            Link = 0,

            /// <summary>Create a .url file.</summary>
            Url,
        }

        /// <summary>
        /// Types of permission setting methods.
        /// </summary>
        private enum PermissionType
        {
            /// <summary>LockPermissions (normal) type permission setting.</summary>
            LockPermissions,

            /// <summary>FileSharePermissions type permission setting.</summary>
            FileSharePermissions,

            /// <summary>SecureObjects type permission setting.</summary>
            SecureObjects,
        }

        /// <summary>
        /// Processes an element for the Compiler.
        /// </summary>
        /// <param name="parentElement">Parent element of element to process.</param>
        /// <param name="element">Element to process.</param>
        /// <param name="context">Extra information about the context in which this element is being parsed.</param>
        public override void ParseElement(Intermediate intermediate, IntermediateSection section, XElement parentElement, XElement element, IDictionary<string, string> context)
        {
            this.ParsePossibleKeyPathElement(intermediate, section, parentElement, element, context);
        }

        /// <summary>
        /// Processes an element for the Compiler.
        /// </summary>
        /// <param name="sourceLineNumbers">Source line number for the parent element.</param>
        /// <param name="parentElement">Parent element of element to process.</param>
        /// <param name="element">Element to process.</param>
        /// <param name="contextValues">Extra information about the context in which this element is being parsed.</param>
        public override ComponentKeyPath ParsePossibleKeyPathElement(Intermediate intermediate, IntermediateSection section, XElement parentElement, XElement element, IDictionary<string, string> context)
        {
            ComponentKeyPath possibleKeyPath = null;

            switch (parentElement.Name.LocalName)
            {
                case "CreateFolder":
                    string createFolderId = context["DirectoryId"];
                    string createFolderComponentId = context["ComponentId"];

                    // If this doesn't parse successfully, something really odd is going on, so let the exception get thrown
                    bool createFolderWin64 = Boolean.Parse(context["Win64"]);

                    switch (element.Name.LocalName)
                    {
                        case "PermissionEx":
                            this.ParsePermissionExElement(intermediate, section, element, createFolderId, createFolderComponentId, createFolderWin64, "CreateFolder");
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(parentElement, element);
                            break;
                    }
                    break;
                case "Component":
                    string componentId = context["ComponentId"];
                    string directoryId = context["DirectoryId"];
                    bool componentWin64 = Boolean.Parse(context["Win64"]);

                    switch (element.Name.LocalName)
                    {
                        case "EventSource":
                            possibleKeyPath = this.ParseEventSourceElement(intermediate, section, element, componentId);
                            break;
                        case "FileShare":
                            this.ParseFileShareElement(intermediate, section, element, componentId, directoryId);
                            break;
                        case "InternetShortcut":
                            this.ParseInternetShortcutElement(intermediate, section, element, componentId, directoryId);
                            break;
                        case "PerformanceCategory":
                            this.ParsePerformanceCategoryElement(intermediate, section, element, componentId);
                            break;
                        case "RemoveFolderEx":
                            this.ParseRemoveFolderExElement(intermediate, section, element, componentId);
                            break;
                        case "RestartResource":
                            this.ParseRestartResourceElement(intermediate, section, element, componentId);
                            break;
                        case "ServiceConfig":
                            this.ParseServiceConfigElement(intermediate, section, element, componentId, "Component", null);
                            break;
                        case "TouchFile":
                            this.ParseTouchFileElement(intermediate, section, element, componentId, componentWin64);
                            break;
                        case "User":
                            this.ParseUserElement(intermediate, section, element, componentId);
                            break;
                        case "XmlFile":
                            this.ParseXmlFileElement(intermediate, section, element, componentId);
                            break;
                        case "XmlConfig":
                            this.ParseXmlConfigElement(intermediate, section, element, componentId, false);
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(parentElement, element);
                            break;
                    }
                    break;
                case "File":
                    string fileId = context["FileId"];
                    string fileComponentId = context["ComponentId"];

                    // If this doesn't parse successfully, something really odd is going on, so let the exception get thrown
                    bool fileWin64 = Boolean.Parse(context["Win64"]);

                    switch (element.Name.LocalName)
                    {
                        case "PerfCounter":
                            this.ParsePerfCounterElement(intermediate, section, element, fileComponentId, fileId);
                            break;
                        case "PermissionEx":
                            this.ParsePermissionExElement(intermediate, section, element, fileId, fileComponentId, fileWin64, "File");
                            break;
                        case "PerfCounterManifest":
                            this.ParsePerfCounterManifestElement(intermediate, section, element, fileComponentId, fileId);
                            break;
                        case "EventManifest":
                            this.ParseEventManifestElement(intermediate, section, element, fileComponentId, fileId);
                            break;
                        case "FormatFile":
                            this.ParseFormatFileElement(intermediate, section, element, fileId, fileWin64);
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(parentElement, element);
                            break;
                    }
                    break;
                case "Bundle":
                case "Fragment":
                case "Module":
                case "Product":
                    switch (element.Name.LocalName)
                    {
                        case "CloseApplication":
                            this.ParseCloseApplicationElement(intermediate, section, element);
                            break;
                        case "Group":
                            this.ParseGroupElement(intermediate, section, element, null);
                            break;
                        case "RestartResource":
                            // Currently not supported for Bundles.
                            if (parentElement.Name.LocalName != "Bundle")
                            {
                                this.ParseRestartResourceElement(intermediate, section, element, null);
                            }
                            else
                            {
                                this.ParseHelper.UnexpectedElement(parentElement, element);
                            }
                            break;
                        case "User":
                            this.ParseUserElement(intermediate, section, element, null);
                            break;
                        case "ComponentSearch":
                        case "ComponentSearchRef":
                        case "DirectorySearch":
                        case "DirectorySearchRef":
                        case "FileSearch":
                        case "FileSearchRef":
                        case "ProductSearch":
                        case "ProductSearchRef":
                        case "RegistrySearch":
                        case "RegistrySearchRef":
                            // These will eventually be supported under Module/Product, but are not yet.
                            if (parentElement.Name.LocalName == "Bundle" || parentElement.Name.LocalName == "Fragment")
                            {
                                // TODO: When these are supported by all section types, move
                                // these out of the nested switch and back into the surrounding one.
                                switch (element.Name.LocalName)
                                {
                                    case "ComponentSearch":
                                        this.ParseComponentSearchElement(intermediate, section, element);
                                        break;
                                    case "ComponentSearchRef":
                                        this.ParseComponentSearchRefElement(intermediate, section, element);
                                        break;
                                    case "DirectorySearch":
                                        this.ParseDirectorySearchElement(intermediate, section, element);
                                        break;
                                    case "DirectorySearchRef":
                                        this.ParseWixSearchRefElement(intermediate, section, element);
                                        break;
                                    case "FileSearch":
                                        this.ParseFileSearchElement(intermediate, section, element);
                                        break;
                                    case "FileSearchRef":
                                        this.ParseWixSearchRefElement(intermediate, section, element);
                                        break;
                                    case "ProductSearch":
                                        this.ParseProductSearchElement(intermediate, section, element);
                                        break;
                                    case "ProductSearchRef":
                                        this.ParseWixSearchRefElement(intermediate, section, element);
                                        break;
                                    case "RegistrySearch":
                                        this.ParseRegistrySearchElement(intermediate, section, element);
                                        break;
                                    case "RegistrySearchRef":
                                        this.ParseWixSearchRefElement(intermediate, section, element);
                                        break;
                                }
                            }
                            else
                            {
                                this.ParseHelper.UnexpectedElement(parentElement, element);
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(parentElement, element);
                            break;
                    }
                    break;
                case "Registry":
                case "RegistryKey":
                case "RegistryValue":
                    string registryId = context["RegistryId"];
                    string registryComponentId = context["ComponentId"];

                    // If this doesn't parse successfully, something really odd is going on, so let the exception get thrown
                    bool registryWin64 = Boolean.Parse(context["Win64"]);

                    switch (element.Name.LocalName)
                    {
                        case "PermissionEx":
                            this.ParsePermissionExElement(intermediate, section, element, registryId, registryComponentId, registryWin64, "Registry");
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(parentElement, element);
                            break;
                    }
                    break;
                case "ServiceInstall":
                    string serviceInstallId = context["ServiceInstallId"];
                    string serviceInstallName = context["ServiceInstallName"];
                    string serviceInstallComponentId = context["ServiceInstallComponentId"];

                    // If this doesn't parse successfully, something really odd is going on, so let the exception get thrown
                    bool serviceInstallWin64 = Boolean.Parse(context["Win64"]);

                    switch (element.Name.LocalName)
                    {
                        case "PermissionEx":
                            this.ParsePermissionExElement(intermediate, section, element, serviceInstallId, serviceInstallComponentId, serviceInstallWin64, "ServiceInstall");
                            break;
                        case "ServiceConfig":
                            this.ParseServiceConfigElement(intermediate, section, element, serviceInstallComponentId, "ServiceInstall", serviceInstallName);
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(parentElement, element);
                            break;
                    }
                    break;
                default:
                    this.ParseHelper.UnexpectedElement(parentElement, element);
                    break;
            }

            return possibleKeyPath;
        }

        /// <summary>
        /// Parses the common search attributes shared across all searches.
        /// </summary>
        /// <param name="sourceLineNumbers">Source line number for the parent element.</param>
        /// <param name="attrib">Attribute to parse.</param>
        /// <param name="id">Value of the Id attribute.</param>
        /// <param name="variable">Value of the Variable attribute.</param>
        /// <param name="condition">Value of the Condition attribute.</param>
        /// <param name="after">Value of the After attribute.</param>
        private void ParseCommonSearchAttributes(SourceLineNumber sourceLineNumbers, XAttribute attrib, ref Identifier id, ref string variable, ref string condition, ref string after)
        {
            switch (attrib.Name.LocalName)
            {
                case "Id":
                    id = this.ParseHelper.GetAttributeIdentifier(sourceLineNumbers, attrib);
                    break;
                case "Variable":
                    variable = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                    // TODO: handle standard bundle variables
                    break;
                case "Condition":
                    condition = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                    break;
                case "After":
                    after = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                    break;
                default:
                    System.Diagnostics.Debug.Assert(false);
                    break;
            }
        }

        /// <summary>
        /// Parses a ComponentSearch element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        private void ParseComponentSearchElement(Intermediate intermediate, IntermediateSection section, XElement element)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            Identifier id = null;
            string variable = null;
            string condition = null;
            string after = null;
            string guid = null;
            string productCode = null;
            Serialize.ComponentSearch.ResultType result = Serialize.ComponentSearch.ResultType.NotSet;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                        case "Variable":
                        case "Condition":
                        case "After":
                            this.ParseCommonSearchAttributes(sourceLineNumbers, attrib, ref id, ref variable, ref condition, ref after);
                            break;
                        case "Guid":
                            guid = this.ParseHelper.GetAttributeGuidValue(sourceLineNumbers, attrib);
                            break;
                        case "ProductCode":
                            productCode = this.ParseHelper.GetAttributeGuidValue(sourceLineNumbers, attrib);
                            break;
                        case "Result":
                            string resultValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            if (!Serialize.ComponentSearch.TryParseResultType(resultValue, out result))
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, attrib.Parent.Name.LocalName, attrib.Name.LocalName,
                                    resultValue,
                                    Serialize.ComponentSearch.ResultType.directory.ToString(),
                                    Serialize.ComponentSearch.ResultType.state.ToString(),
                                    Serialize.ComponentSearch.ResultType.keyPath.ToString()));
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == variable)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Variable"));
            }

            if (null == guid)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Guid"));
            }

            if (null == id)
            {
                id = this.ParseHelper.CreateIdentifier("wcs", variable, condition, after, guid, productCode, result.ToString());
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                this.CreateWixSearchRow(section, sourceLineNumbers, id, variable, condition);
                if (after != null)
                {
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "WixSearch", after);
                    // TODO: We're currently defaulting to "always run after", which we will need to change...
                    this.CreateWixSearchRelationRow(section, sourceLineNumbers, id, after, 2);
                }

                WixComponentSearchAttributes attributes = WixComponentSearchAttributes.KeyPath;
                switch (result)
                {
                    case Serialize.ComponentSearch.ResultType.directory:
                        attributes = WixComponentSearchAttributes.WantDirectory;
                        break;
                    case Serialize.ComponentSearch.ResultType.keyPath:
                        attributes = WixComponentSearchAttributes.KeyPath;
                        break;
                    case Serialize.ComponentSearch.ResultType.state:
                        attributes = WixComponentSearchAttributes.State;
                        break;
                }

                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixComponentSearch", id);
                row.Set(1, guid);
                row.Set(2, productCode);
                row.Set(3, (int)attributes);
            }
        }

        /// <summary>
        /// Parses a ComponentSearchRef element
        /// </summary>
        /// <param name="element">Element to parse.</param>
        private void ParseComponentSearchRefElement(Intermediate intermediate, IntermediateSection section, XElement element)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string refId = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            refId = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "WixComponentSearch", refId);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);
        }

        /// <summary>
        /// Parses an event source element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        private ComponentKeyPath ParseEventSourceElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string sourceName = null;
            string logName = null;
            string categoryMessageFile = null;
            int categoryCount = CompilerConstants.IntegerNotSet;
            string eventMessageFile = null;
            string parameterMessageFile = null;
            int typesSupported = 0;
            bool isKeyPath = false;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "CategoryCount":
                            categoryCount = this.ParseHelper.GetAttributeIntegerValue(sourceLineNumbers, attrib, 0, int.MaxValue);
                            break;
                        case "CategoryMessageFile":
                            categoryMessageFile = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "EventMessageFile":
                            eventMessageFile = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "KeyPath":
                            isKeyPath = YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            break;
                        case "Log":
                            logName = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            if ("Security" == logName)
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName, logName, "Application", "System", "<customEventLog>"));
                            }
                            break;
                        case "Name":
                            sourceName = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ParameterMessageFile":
                            parameterMessageFile = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "SupportsErrors":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                typesSupported |= 0x01; // EVENTLOG_ERROR_TYPE
                            }
                            break;
                        case "SupportsFailureAudits":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                typesSupported |= 0x10; // EVENTLOG_AUDIT_FAILURE
                            }
                            break;
                        case "SupportsInformationals":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                typesSupported |= 0x04; // EVENTLOG_INFORMATION_TYPE
                            }
                            break;
                        case "SupportsSuccessAudits":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                typesSupported |= 0x08; // EVENTLOG_AUDIT_SUCCESS
                            }
                            break;
                        case "SupportsWarnings":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                typesSupported |= 0x02; // EVENTLOG_WARNING_TYPE
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == sourceName)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Name"));
            }

            if (null == logName)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "EventLog"));
            }

            if (null == eventMessageFile)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "EventMessageFile"));
            }

            if (null == categoryMessageFile && 0 < categoryCount)
            {
                this.Messaging.Write(ErrorMessages.IllegalAttributeWithoutOtherAttributes(sourceLineNumbers, element.Name.LocalName, "CategoryCount", "CategoryMessageFile"));
            }

            if (null != categoryMessageFile && CompilerConstants.IntegerNotSet == categoryCount)
            {
                this.Messaging.Write(ErrorMessages.IllegalAttributeWithoutOtherAttributes(sourceLineNumbers, element.Name.LocalName, "CategoryMessageFile", "CategoryCount"));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            int registryRoot = 2; // MsiInterop.MsidbRegistryRootLocalMachine 
            string eventSourceKey = String.Format(@"SYSTEM\CurrentControlSet\Services\EventLog\{0}\{1}", logName, sourceName);
            Identifier id = this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, eventSourceKey, "EventMessageFile", String.Concat("#%", eventMessageFile), componentId, false);

            if (null != categoryMessageFile)
            {
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, eventSourceKey, "CategoryMessageFile", String.Concat("#%", categoryMessageFile), componentId, false);
            }

            if (CompilerConstants.IntegerNotSet != categoryCount)
            {
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, eventSourceKey, "CategoryCount", String.Concat("#", categoryCount), componentId, false);
            }

            if (null != parameterMessageFile)
            {
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, eventSourceKey, "ParameterMessageFile", String.Concat("#%", parameterMessageFile), componentId, false);
            }

            if (0 != typesSupported)
            {
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, eventSourceKey, "TypesSupported", String.Concat("#", typesSupported), componentId, false);
            }

            return new ComponentKeyPath() { Id = id.Id, Explicit = isKeyPath, Type = ComponentKeyPathType.Registry };
        }

        /// <summary>
        /// Parses a close application element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        private void ParseCloseApplicationElement(Intermediate intermediate, IntermediateSection section, XElement element)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string condition = null;
            string description = null;
            string target = null;
            string property = null;
            Identifier id = null;
            int attributes = 2; // default to CLOSEAPP_ATTRIBUTE_REBOOTPROMPT enabled
            int sequence = CompilerConstants.IntegerNotSet;
            int terminateExitCode = CompilerConstants.IntegerNotSet;
            int timeout = CompilerConstants.IntegerNotSet;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifier(sourceLineNumbers, attrib);
                            break;
                        case "Description":
                            description = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Property":
                            property = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            break;
                        case "Sequence":
                            sequence = this.ParseHelper.GetAttributeIntegerValue(sourceLineNumbers, attrib, 0, int.MaxValue);
                            break;
                        case "Timeout":
                            timeout = this.ParseHelper.GetAttributeIntegerValue(sourceLineNumbers, attrib, 0, int.MaxValue);
                            break;
                        case "Target":
                            target = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "CloseMessage":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= 1; // CLOSEAPP_ATTRIBUTE_CLOSEMESSAGE
                            }
                            else
                            {
                                attributes &= ~1; // CLOSEAPP_ATTRIBUTE_CLOSEMESSAGE
                            }
                            break;
                        case "EndSessionMessage":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= 8; // CLOSEAPP_ATTRIBUTE_ENDSESSIONMESSAGE
                            }
                            else
                            {
                                attributes &= ~8; // CLOSEAPP_ATTRIBUTE_ENDSESSIONMESSAGE
                            }
                            break;
                        case "PromptToContinue":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= 0x40; // CLOSEAPP_ATTRIBUTE_PROMPTTOCONTINUE
                            }
                            else
                            {
                                attributes &= ~0x40; // CLOSEAPP_ATTRIBUTE_PROMPTTOCONTINUE
                            }
                            break;
                        case "RebootPrompt":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= 2; // CLOSEAPP_ATTRIBUTE_REBOOTPROMPT
                            }
                            else
                            {
                                attributes &= ~2; // CLOSEAPP_ATTRIBUTE_REBOOTPROMPT
                            }
                            break;
                        case "ElevatedCloseMessage":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= 4; // CLOSEAPP_ATTRIBUTE_ELEVATEDCLOSEMESSAGE
                            }
                            else
                            {
                                attributes &= ~4; // CLOSEAPP_ATTRIBUTE_ELEVATEDCLOSEMESSAGE
                            }
                            break;
                        case "ElevatedEndSessionMessage":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= 0x10; // CLOSEAPP_ATTRIBUTE_ELEVATEDENDSESSIONMESSAGE
                            }
                            else
                            {
                                attributes &= ~0x10; // CLOSEAPP_ATTRIBUTE_ELEVATEDENDSESSIONMESSAGE
                            }
                            break;
                        case "TerminateProcess":
                            terminateExitCode = this.ParseHelper.GetAttributeIntegerValue(sourceLineNumbers, attrib, 0, int.MaxValue);
                            attributes |= 0x20; // CLOSEAPP_ATTRIBUTE_TERMINATEPROCESS
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == target)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Target"));
            }
            else if (null == id)
            {
                id = this.ParseHelper.CreateIdentifier("ca", target);
            }

            if (String.IsNullOrEmpty(description) && 0x40 == (attributes & 0x40))
            {
                this.Messaging.Write(ErrorMessages.IllegalAttributeValueWithoutOtherAttribute(sourceLineNumbers, element.Name.LocalName, "PromptToContinue", "yes", "Description"));
            }

            if (0x22 == (attributes & 0x22))
            {
                this.Messaging.Write(ErrorMessages.IllegalAttributeWithOtherAttribute(sourceLineNumbers, element.Name.LocalName, "TerminateProcess", "RebootPrompt", "yes"));
            }

            // get the condition from the inner text of the element
            condition = this.ParseHelper.GetConditionInnerText(element);

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            // Reference CustomAction since nothing will happen without it
            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CA is referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixCloseApplications_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixCloseApplications");
            }

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixCloseApplication", id);
                row.Set(1, target);
                row.Set(2, description);
                row.Set(3, condition);
                row.Set(4, attributes);
                if (CompilerConstants.IntegerNotSet != sequence)
                {
                    row.Set(5, sequence);
                }
                row.Set(6, property);
                if (CompilerConstants.IntegerNotSet != terminateExitCode)
                {
                    row.Set(7, terminateExitCode);
                }
                if (CompilerConstants.IntegerNotSet != timeout)
                {
                    row.Set(8, timeout * 1000); // make the timeout milliseconds in the table.
                }
            }
        }

        /// <summary>
        /// Parses a DirectorySearch element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        private void ParseDirectorySearchElement(Intermediate intermediate, IntermediateSection section, XElement element)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            Identifier id = null;
            string variable = null;
            string condition = null;
            string after = null;
            string path = null;
            Serialize.DirectorySearch.ResultType result = Serialize.DirectorySearch.ResultType.NotSet;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                        case "Variable":
                        case "Condition":
                        case "After":
                            this.ParseCommonSearchAttributes(sourceLineNumbers, attrib, ref id, ref variable, ref condition, ref after);
                            break;
                        case "Path":
                            path = this.ParseHelper.GetAttributeLongFilename(sourceLineNumbers, attrib, false, true);
                            break;
                        case "Result":
                            string resultValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            if (!Serialize.DirectorySearch.TryParseResultType(resultValue, out result))
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, attrib.Parent.Name.LocalName, attrib.Name.LocalName,
                                    resultValue, Serialize.DirectorySearch.ResultType.exists.ToString()));
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == variable)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Variable"));
            }

            if (null == path)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Path"));
            }

            if (null == id)
            {
                id = this.ParseHelper.CreateIdentifier("wds", variable, condition, after, path, result.ToString());
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                this.CreateWixSearchRow(section, sourceLineNumbers, id, variable, condition);
                if (after != null)
                {
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "WixSearch", after);
                    // TODO: We're currently defaulting to "always run after", which we will need to change...
                    this.CreateWixSearchRelationRow(section, sourceLineNumbers, id, after, 2);
                }

                WixFileSearchAttributes attributes = WixFileSearchAttributes.IsDirectory;
                switch (result)
                {
                    case Serialize.DirectorySearch.ResultType.exists:
                        attributes |= WixFileSearchAttributes.WantExists;
                        break;
                }

                this.CreateWixFileSearchRow(section, sourceLineNumbers, id, path, attributes);
            }
        }

        /// <summary>
        /// Parses a DirectorySearchRef, FileSearchRef, ProductSearchRef, and RegistrySearchRef elements
        /// </summary>
        /// <param name="node">Element to parse.</param>
        private void ParseWixSearchRefElement(Intermediate intermediate, IntermediateSection section, XElement node)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(node);
            string refId = null;

            foreach (XAttribute attrib in node.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            refId = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "WixSearch", refId);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(node, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, node, attrib);
                }
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, node);
        }

        /// <summary>
        /// Parses a FileSearch element.
        /// </summary>
        /// <param name="node">Element to parse.</param>
        private void ParseFileSearchElement(Intermediate intermediate, IntermediateSection section, XElement node)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(node);
            Identifier id = null;
            string variable = null;
            string condition = null;
            string after = null;
            string path = null;
            Serialize.FileSearch.ResultType result = Serialize.FileSearch.ResultType.NotSet;

            foreach (XAttribute attrib in node.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                        case "Variable":
                        case "Condition":
                        case "After":
                            this.ParseCommonSearchAttributes(sourceLineNumbers, attrib, ref id, ref variable, ref condition, ref after);
                            break;
                        case "Path":
                            path = this.ParseHelper.GetAttributeLongFilename(sourceLineNumbers, attrib, false, true);
                            break;
                        case "Result":
                            string resultValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            if (!Serialize.FileSearch.TryParseResultType(resultValue, out result))
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, attrib.Parent.Name.LocalName, attrib.Name.LocalName,
                                    resultValue,
                                    Serialize.FileSearch.ResultType.exists.ToString(),
                                    Serialize.FileSearch.ResultType.version.ToString()));
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(node, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, node, attrib);
                }
            }

            if (null == variable)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, node.Name.LocalName, "Variable"));
            }

            if (null == path)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, node.Name.LocalName, "Path"));
            }

            if (null == id)
            {
                id = this.ParseHelper.CreateIdentifier("wfs", variable, condition, after, path, result.ToString());
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, node);

            if (!this.Messaging.EncounteredError)
            {
                this.CreateWixSearchRow(section, sourceLineNumbers, id, variable, condition);
                if (after != null)
                {
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "WixSearch", after);
                    // TODO: We're currently defaulting to "always run after", which we will need to change...
                    this.CreateWixSearchRelationRow(section, sourceLineNumbers, id, after, 2);
                }

                WixFileSearchAttributes attributes = WixFileSearchAttributes.Default;
                switch (result)
                {
                    case Serialize.FileSearch.ResultType.exists:
                        attributes |= WixFileSearchAttributes.WantExists;
                        break;
                    case Serialize.FileSearch.ResultType.version:
                        attributes |= WixFileSearchAttributes.WantVersion;
                        break;
                }

                this.CreateWixFileSearchRow(section, sourceLineNumbers, id, path, attributes);
            }
        }

        /// <summary>
        /// Creates a row in the WixFileSearch table.
        /// </summary>
        /// <param name="sourceLineNumbers">Source line number for the parent element.</param>
        /// <param name="id">Identifier of the search (key into the WixSearch table)</param>
        /// <param name="path">File/directory path to search for.</param>
        /// <param name="attributes"></param>
        private void CreateWixFileSearchRow(IntermediateSection section, SourceLineNumber sourceLineNumbers, Identifier id, string path, WixFileSearchAttributes attributes)
        {
            var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixFileSearch", id);
            row.Set(1, path);
            //row.Set(2, minVersion;
            //row.Set(3, maxVersion;
            //row.Set(4, minSize;
            //row.Set(5, maxSize;
            //row.Set(6, minDate;
            //row.Set(7, maxDate;
            //row.Set(8, languages;
            row.Set(9, (int)attributes);
        }

        /// <summary>
        /// Creates a row in the WixSearch table.
        /// </summary>
        /// <param name="sourceLineNumbers">Source line number for the parent element.</param>
        /// <param name="id">Identifier of the search.</param>
        /// <param name="variable">The Burn variable to store the result into.</param>
        /// <param name="condition">A condition to test before evaluating the search.</param>
        private void CreateWixSearchRow(IntermediateSection section, SourceLineNumber sourceLineNumbers, Identifier id, string variable, string condition)
        {
            var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixSearch", id);
            row.Set(1, variable);
            row.Set(2, condition);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sourceLineNumbers">Source line number for the parent element.</param>
        /// <param name="id">Identifier of the search (key into the WixSearch table)</param>
        /// <param name="parentId">Identifier of the search that comes before (key into the WixSearch table)</param>
        /// <param name="attributes">Further details about the relation between id and parentId.</param>
        private void CreateWixSearchRelationRow(IntermediateSection section, SourceLineNumber sourceLineNumbers, Identifier id, string parentId, int attributes)
        {
            var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixSearchRelation", id);
            row.Set(1, parentId);
            row.Set(2, attributes);
        }

        /// <summary>
        /// Parses a file share element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        /// <param name="directoryId">Identifier of referred to directory.</param>
        private void ParseFileShareElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId, string directoryId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string description = null;
            string name = null;
            string id = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            break;
                        case "Name":
                            name = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Description":
                            description = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == id)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Id"));
            }

            if (null == name)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Name"));
            }

            if (!element.Elements().Any())
            {
                this.Messaging.Write(ErrorMessages.ExpectedElement(sourceLineNumbers, element.Name.LocalName, "FileSharePermission"));
            }

            foreach (XElement child in element.Elements())
            {
                if (this.Namespace == child.Name.Namespace)
                {
                    switch (child.Name.LocalName)
                    {
                        case "FileSharePermission":
                            this.ParseFileSharePermissionElement(intermediate, section, child, id);
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(element, child);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionElement(this.Context.Extensions, intermediate, section, element, child);
                }
            }

            // Reference ConfigureSmbInstall and ConfigureSmbUninstall since nothing will happen without it
            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CA is referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureSmbInstall_ARM");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureSmbUninstall_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureSmbInstall");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureSmbUninstall");
            }

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "FileShare");
                row.Set(0, id);
                row.Set(1, name);
                row.Set(2, componentId);
                row.Set(3, description);
                row.Set(4, directoryId);
            }
        }

        /// <summary>
        /// Parses a FileSharePermission element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="fileShareId">The identifier of the parent FileShare element.</param>
        private void ParseFileSharePermissionElement(Intermediate intermediate, IntermediateSection section, XElement element, string fileShareId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            BitArray bits = new BitArray(32);
            int permission = 0;
            string user = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "User":
                            user = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "User", user);
                            break;
                        default:
                            YesNoType attribValue = this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            if (!this.TrySetBitFromName(UtilConstants.StandardPermissions, attrib.Name.LocalName, attribValue, bits, 16))
                            {
                                if (!this.TrySetBitFromName(UtilConstants.GenericPermissions, attrib.Name.LocalName, attribValue, bits, 28))
                                {
                                    if (!this.TrySetBitFromName(UtilConstants.FolderPermissions, attrib.Name.LocalName, attribValue, bits, 0))
                                    {
                                        this.ParseHelper.UnexpectedAttribute(element, attrib);
                                        break;
                                    }
                                }
                            }
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            permission = this.CreateIntegerFromBitArray(bits);

            if (null == user)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "User"));
            }

            if (int.MinValue == permission) // just GENERIC_READ, which is MSI_NULL
            {
                this.Messaging.Write(ErrorMessages.GenericReadNotAllowed(sourceLineNumbers));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "FileSharePermissions");
                row.Set(0, fileShareId);
                row.Set(1, user);
                row.Set(2, permission);
            }
        }

        /// <summary>
        /// Parses a group element.
        /// </summary>
        /// <param name="element">Node to be parsed.</param>
        /// <param name="componentId">Component Id of the parent component of this element.</param>
        private void ParseGroupElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string id = null;
            string domain = null;
            string name = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            break;
                        case "Name":
                            name = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Domain":
                            domain = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == id)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Id"));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "Group");
                row.Set(0, id);
                row.Set(1, componentId);
                row.Set(2, name);
                row.Set(3, domain);
            }
        }

        /// <summary>
        /// Parses a GroupRef element
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="userId">Required user id to be joined to the group.</param>
        private void ParseGroupRefElement(Intermediate intermediate, IntermediateSection section, XElement element, string userId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string groupId = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            groupId = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "Group", groupId);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "UserGroup");
                row.Set(0, userId);
                row.Set(1, groupId);
            }
        }

        /// <summary>
        /// Parses an InternetShortcut element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        /// <param name="defaultTarget">Default directory if none is specified on the InternetShortcut element.</param>
        private void ParseInternetShortcutElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId, string defaultTarget)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string id = null;
            string name = null;
            string target = null;
            string directoryId = null;
            string type = null;
            string iconFile = null;
            int iconIndex = 0;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Directory":
                            directoryId = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            break;
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            break;
                        case "Name":
                            name = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Target":
                            target = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Type":
                            type = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "IconFile":
                            iconFile = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "IconIndex":
                            iconIndex = this.ParseHelper.GetAttributeIntegerValue(sourceLineNumbers, attrib, 0, int.MaxValue);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            // If there was no directoryId specified on the InternetShortcut element, default to the one on
            // the parent component.
            if (null == directoryId)
            {
                directoryId = defaultTarget;
            }

            if (null == id)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Id"));
            }

            // In theory this can never be the case, since InternetShortcut can only be under
            // a component element, and if the Directory wasn't specified the default will come
            // from the component. However, better safe than sorry, so here's a check to make sure
            // it didn't wind up being null after setting it to the defaultTarget.
            if (null == directoryId)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Directory"));
            }

            if (null == name)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Name"));
            }

            if (null == target)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Target"));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            InternetShortcutType shortcutType = InternetShortcutType.Link;
            if (0 == String.Compare(type, "url", StringComparison.OrdinalIgnoreCase))
            {
                shortcutType = InternetShortcutType.Url;
            }

            if (!this.Messaging.EncounteredError)
            {
                CreateWixInternetShortcut(intermediate, section, sourceLineNumbers, componentId, directoryId, id, name, target, shortcutType, iconFile, iconIndex);
            }
        }

        /// <summary>
        /// Creates the rows needed for WixInternetShortcut to work.
        /// </summary>
        /// <param name="core">The CompilerCore object used to create rows.</param>
        /// <param name="sourceLineNumbers">Source line information about the owner element.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        /// <param name="directoryId">Identifier of directory containing shortcut.</param>
        /// <param name="id">Identifier of shortcut.</param>
        /// <param name="name">Name of shortcut without extension.</param>
        /// <param name="target">Target URL of shortcut.</param>
        public void CreateWixInternetShortcut(Intermediate intermediate, IntermediateSection section, SourceLineNumber sourceLineNumbers, string componentId, string directoryId, string shortcutId, string name, string target, InternetShortcutType type, string iconFile, int iconIndex)
        {
            // add the appropriate extension based on type of shortcut
            name = String.Concat(name, InternetShortcutType.Url == type ? ".url" : ".lnk");

            var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixInternetShortcut");
            row.Set(0, shortcutId);
            row.Set(1, componentId);
            row.Set(2, directoryId);
            row.Set(3, name);
            row.Set(4, target);
            row.Set(5, (int)type);
            row.Set(6, iconFile);
            row.Set(7, iconIndex);

            // Reference custom action because nothing will happen without it
            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CA is referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixSchedInternetShortcuts_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixSchedInternetShortcuts");
            }

            // make sure we have a CreateFolder table so that the immediate CA can add temporary rows to handle installation and uninstallation
            this.ParseHelper.EnsureTable(section, sourceLineNumbers, "CreateFolder");

            // use built-in MSI functionality to remove the shortcuts rather than doing so via CA
            row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "RemoveFile");
            row.Set(0, shortcutId);
            row.Set(1, componentId);
            row.Set(2, this.ParseHelper.IsValidShortFilename(name, false) ? name : String.Concat(this.ParseHelper.CreateShortName(name, true, false, directoryId, name), "|", name));
            row.Set(3, directoryId);
            row.Set(4, 2); // msidbRemoveFileInstallModeOnRemove
        }

        /// <summary>
        /// Parses a performance category element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        private void ParsePerformanceCategoryElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string id = null;
            string name = null;
            string help = null;
            YesNoType multiInstance = YesNoType.No;
            int defaultLanguage = 0x09; // default to "english"

            ArrayList parsedPerformanceCounters = new ArrayList();

            // default to managed performance counter
            string library = "netfxperf.dll";
            string openEntryPoint = "OpenPerformanceData";
            string collectEntryPoint = "CollectPerformanceData";
            string closeEntryPoint = "ClosePerformanceData";

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Close":
                            closeEntryPoint = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Collect":
                            collectEntryPoint = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "DefaultLanguage":
                            defaultLanguage = this.GetPerformanceCounterLanguage(sourceLineNumbers, attrib);
                            break;
                        case "Help":
                            help = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            break;
                        case "Library":
                            library = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "MultiInstance":
                            multiInstance = this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            break;
                        case "Name":
                            name = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Open":
                            openEntryPoint = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == id)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Id"));
            }

            if (null == name)
            {
                name = id;
            }

            // Process the child counter elements.
            foreach (XElement child in element.Elements())
            {
                if (this.Namespace == child.Name.Namespace)
                {
                    switch (child.Name.LocalName)
                    {
                        case "PerformanceCounter":
                            ParsedPerformanceCounter counter = this.ParsePerformanceCounterElement(intermediate, section, child, defaultLanguage);
                            if (null != counter)
                            {
                                parsedPerformanceCounters.Add(counter);
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(element, child);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionElement(this.Context.Extensions, intermediate, section, element, child);
                }
            }


            if (!this.Messaging.EncounteredError)
            {
                // Calculate the ini and h file content.
                string objectName = "OBJECT_1";
                string objectLanguage = defaultLanguage.ToString("D3", CultureInfo.InvariantCulture);

                StringBuilder sbIniData = new StringBuilder();
                sbIniData.AppendFormat("[info]\r\ndrivername={0}\r\nsymbolfile=wixperf.h\r\n\r\n[objects]\r\n{1}_{2}_NAME=\r\n\r\n[languages]\r\n{2}=LANG{2}\r\n\r\n", name, objectName, objectLanguage);
                sbIniData.AppendFormat("[text]\r\n{0}_{1}_NAME={2}\r\n", objectName, objectLanguage, name);
                if (null != help)
                {
                    sbIniData.AppendFormat("{0}_{1}_HELP={2}\r\n", objectName, objectLanguage, help);
                }

                int symbolConstantsCounter = 0;
                StringBuilder sbSymbolicConstants = new StringBuilder();
                sbSymbolicConstants.AppendFormat("#define {0}    {1}\r\n", objectName, symbolConstantsCounter);

                StringBuilder sbCounterNames = new StringBuilder("[~]");
                StringBuilder sbCounterTypes = new StringBuilder("[~]");
                for (int i = 0; i < parsedPerformanceCounters.Count; ++i)
                {
                    ParsedPerformanceCounter counter = (ParsedPerformanceCounter)parsedPerformanceCounters[i];
                    string counterName = String.Concat("DEVICE_COUNTER_", i + 1);

                    sbIniData.AppendFormat("{0}_{1}_NAME={2}\r\n", counterName, counter.Language, counter.Name);
                    if (null != counter.Help)
                    {
                        sbIniData.AppendFormat("{0}_{1}_HELP={2}\r\n", counterName, counter.Language, counter.Help);
                    }

                    symbolConstantsCounter += 2;
                    sbSymbolicConstants.AppendFormat("#define {0}    {1}\r\n", counterName, symbolConstantsCounter);

                    sbCounterNames.Append(UtilCompiler.FindPropertyBrackets.Replace(counter.Name, this.EscapeProperties));
                    sbCounterNames.Append("[~]");
                    sbCounterTypes.Append(counter.Type);
                    sbCounterTypes.Append("[~]");
                }

                sbSymbolicConstants.AppendFormat("#define LAST_{0}_COUNTER_OFFSET    {1}\r\n", objectName, symbolConstantsCounter);

                // Add the calculated INI and H strings to the PerformanceCategory table.
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "PerformanceCategory");
                row.Set(0, id);
                row.Set(1, componentId);
                row.Set(2, name);
                row.Set(3, sbIniData.ToString());
                row.Set(4, sbSymbolicConstants.ToString());

                // Set up the application's performance key.
                int registryRoot = 2; // HKLM
                string escapedName = UtilCompiler.FindPropertyBrackets.Replace(name, this.EscapeProperties);
                string linkageKey = String.Format(@"SYSTEM\CurrentControlSet\Services\{0}\Linkage", escapedName);
                string performanceKey = String.Format(@"SYSTEM\CurrentControlSet\Services\{0}\Performance", escapedName);

                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, linkageKey, "Export", escapedName, componentId, false);
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, performanceKey, "-", null, componentId, false);
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, performanceKey, "Library", library, componentId, false);
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, performanceKey, "Open", openEntryPoint, componentId, false);
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, performanceKey, "Collect", collectEntryPoint, componentId, false);
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, performanceKey, "Close", closeEntryPoint, componentId, false);
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, performanceKey, "IsMultiInstance", YesNoType.Yes == multiInstance ? "#1" : "#0", componentId, false);
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, performanceKey, "Counter Names", sbCounterNames.ToString(), componentId, false);
                this.ParseHelper.CreateRegistryRow(section, sourceLineNumbers, registryRoot, performanceKey, "Counter Types", sbCounterTypes.ToString(), componentId, false);
            }

            // Reference InstallPerfCounterData and UninstallPerfCounterData since nothing will happen without them
            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CAs are referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "InstallPerfCounterData_ARM");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "UninstallPerfCounterData_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "InstallPerfCounterData");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "UninstallPerfCounterData");
            }
        }

        /// <summary>
        /// Gets the performance counter language as a decimal number.
        /// </summary>
        /// <param name="sourceLineNumbers">Source line information about the owner element.</param>
        /// <param name="attribute">The attribute containing the value to get.</param>
        /// <returns>Numeric representation of the language as per WinNT.h.</returns>
        private int GetPerformanceCounterLanguage(SourceLineNumber sourceLineNumbers, XAttribute attribute)
        {
            int language = 0;
            if (String.Empty == attribute.Value)
            {
                this.Messaging.Write(ErrorMessages.IllegalEmptyAttributeValue(sourceLineNumbers, attribute.Parent.Name.LocalName, attribute.Name.LocalName));
            }
            else
            {
                switch (attribute.Value)
                {
                    case "afrikaans":
                        language = 0x36;
                        break;
                    case "albanian":
                        language = 0x1c;
                        break;
                    case "arabic":
                        language = 0x01;
                        break;
                    case "armenian":
                        language = 0x2b;
                        break;
                    case "assamese":
                        language = 0x4d;
                        break;
                    case "azeri":
                        language = 0x2c;
                        break;
                    case "basque":
                        language = 0x2d;
                        break;
                    case "belarusian":
                        language = 0x23;
                        break;
                    case "bengali":
                        language = 0x45;
                        break;
                    case "bulgarian":
                        language = 0x02;
                        break;
                    case "catalan":
                        language = 0x03;
                        break;
                    case "chinese":
                        language = 0x04;
                        break;
                    case "croatian":
                        language = 0x1a;
                        break;
                    case "czech":
                        language = 0x05;
                        break;
                    case "danish":
                        language = 0x06;
                        break;
                    case "divehi":
                        language = 0x65;
                        break;
                    case "dutch":
                        language = 0x13;
                        break;
                    case "piglatin":
                    case "english":
                        language = 0x09;
                        break;
                    case "estonian":
                        language = 0x25;
                        break;
                    case "faeroese":
                        language = 0x38;
                        break;
                    case "farsi":
                        language = 0x29;
                        break;
                    case "finnish":
                        language = 0x0b;
                        break;
                    case "french":
                        language = 0x0c;
                        break;
                    case "galician":
                        language = 0x56;
                        break;
                    case "georgian":
                        language = 0x37;
                        break;
                    case "german":
                        language = 0x07;
                        break;
                    case "greek":
                        language = 0x08;
                        break;
                    case "gujarati":
                        language = 0x47;
                        break;
                    case "hebrew":
                        language = 0x0d;
                        break;
                    case "hindi":
                        language = 0x39;
                        break;
                    case "hungarian":
                        language = 0x0e;
                        break;
                    case "icelandic":
                        language = 0x0f;
                        break;
                    case "indonesian":
                        language = 0x21;
                        break;
                    case "italian":
                        language = 0x10;
                        break;
                    case "japanese":
                        language = 0x11;
                        break;
                    case "kannada":
                        language = 0x4b;
                        break;
                    case "kashmiri":
                        language = 0x60;
                        break;
                    case "kazak":
                        language = 0x3f;
                        break;
                    case "konkani":
                        language = 0x57;
                        break;
                    case "korean":
                        language = 0x12;
                        break;
                    case "kyrgyz":
                        language = 0x40;
                        break;
                    case "latvian":
                        language = 0x26;
                        break;
                    case "lithuanian":
                        language = 0x27;
                        break;
                    case "macedonian":
                        language = 0x2f;
                        break;
                    case "malay":
                        language = 0x3e;
                        break;
                    case "malayalam":
                        language = 0x4c;
                        break;
                    case "manipuri":
                        language = 0x58;
                        break;
                    case "marathi":
                        language = 0x4e;
                        break;
                    case "mongolian":
                        language = 0x50;
                        break;
                    case "nepali":
                        language = 0x61;
                        break;
                    case "norwegian":
                        language = 0x14;
                        break;
                    case "oriya":
                        language = 0x48;
                        break;
                    case "polish":
                        language = 0x15;
                        break;
                    case "portuguese":
                        language = 0x16;
                        break;
                    case "punjabi":
                        language = 0x46;
                        break;
                    case "romanian":
                        language = 0x18;
                        break;
                    case "russian":
                        language = 0x19;
                        break;
                    case "sanskrit":
                        language = 0x4f;
                        break;
                    case "serbian":
                        language = 0x1a;
                        break;
                    case "sindhi":
                        language = 0x59;
                        break;
                    case "slovak":
                        language = 0x1b;
                        break;
                    case "slovenian":
                        language = 0x24;
                        break;
                    case "spanish":
                        language = 0x0a;
                        break;
                    case "swahili":
                        language = 0x41;
                        break;
                    case "swedish":
                        language = 0x1d;
                        break;
                    case "syriac":
                        language = 0x5a;
                        break;
                    case "tamil":
                        language = 0x49;
                        break;
                    case "tatar":
                        language = 0x44;
                        break;
                    case "telugu":
                        language = 0x4a;
                        break;
                    case "thai":
                        language = 0x1e;
                        break;
                    case "turkish":
                        language = 0x1f;
                        break;
                    case "ukrainian":
                        language = 0x22;
                        break;
                    case "urdu":
                        language = 0x20;
                        break;
                    case "uzbek":
                        language = 0x43;
                        break;
                    case "vietnamese":
                        language = 0x2a;
                        break;
                    default:
                        this.Messaging.Write(ErrorMessages.IllegalEmptyAttributeValue(sourceLineNumbers, attribute.Parent.Name.LocalName, attribute.Name.LocalName));
                        break;
                }
            }

            return language;
        }

        /// <summary>
        /// Parses a performance counter element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="defaultLanguage">Default language for the performance counter.</param>
        private ParsedPerformanceCounter ParsePerformanceCounterElement(Intermediate intermediate, IntermediateSection section, XElement element, int defaultLanguage)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            ParsedPerformanceCounter parsedPerformanceCounter = null;
            string name = null;
            string help = null;
            System.Diagnostics.PerformanceCounterType type = System.Diagnostics.PerformanceCounterType.NumberOfItems32;
            int language = defaultLanguage;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Help":
                            help = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Name":
                            name = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Type":
                            type = this.GetPerformanceCounterType(sourceLineNumbers, attrib);
                            break;
                        case "Language":
                            language = this.GetPerformanceCounterLanguage(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == name)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Name"));
            }

            if (null == help)
            {
                this.Messaging.Write(UtilWarnings.RequiredAttributeForWindowsXP(sourceLineNumbers, element.Name.LocalName, "Help"));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                parsedPerformanceCounter = new ParsedPerformanceCounter(name, help, type, language);
            }

            return parsedPerformanceCounter;
        }

        /// <summary>
        /// Gets the performance counter type.
        /// </summary>
        /// <param name="sourceLineNumbers">Source line information about the owner element.</param>
        /// <param name="attribute">The attribute containing the value to get.</param>
        /// <returns>Numeric representation of the language as per WinNT.h.</returns>
        private System.Diagnostics.PerformanceCounterType GetPerformanceCounterType(SourceLineNumber sourceLineNumbers, XAttribute attribute)
        {
            System.Diagnostics.PerformanceCounterType type = System.Diagnostics.PerformanceCounterType.NumberOfItems32;
            if (String.Empty == attribute.Value)
            {
                this.Messaging.Write(ErrorMessages.IllegalEmptyAttributeValue(sourceLineNumbers, attribute.Parent.Name.LocalName, attribute.Name.LocalName));
            }
            else
            {
                switch (attribute.Value)
                {
                    case "averageBase":
                        type = System.Diagnostics.PerformanceCounterType.AverageBase;
                        break;
                    case "averageCount64":
                        type = System.Diagnostics.PerformanceCounterType.AverageCount64;
                        break;
                    case "averageTimer32":
                        type = System.Diagnostics.PerformanceCounterType.AverageTimer32;
                        break;
                    case "counterDelta32":
                        type = System.Diagnostics.PerformanceCounterType.CounterDelta32;
                        break;
                    case "counterTimerInverse":
                        type = System.Diagnostics.PerformanceCounterType.CounterTimerInverse;
                        break;
                    case "sampleFraction":
                        type = System.Diagnostics.PerformanceCounterType.SampleFraction;
                        break;
                    case "timer100Ns":
                        type = System.Diagnostics.PerformanceCounterType.Timer100Ns;
                        break;
                    case "counterTimer":
                        type = System.Diagnostics.PerformanceCounterType.CounterTimer;
                        break;
                    case "rawFraction":
                        type = System.Diagnostics.PerformanceCounterType.RawFraction;
                        break;
                    case "timer100NsInverse":
                        type = System.Diagnostics.PerformanceCounterType.Timer100NsInverse;
                        break;
                    case "counterMultiTimer":
                        type = System.Diagnostics.PerformanceCounterType.CounterMultiTimer;
                        break;
                    case "counterMultiTimer100Ns":
                        type = System.Diagnostics.PerformanceCounterType.CounterMultiTimer100Ns;
                        break;
                    case "counterMultiTimerInverse":
                        type = System.Diagnostics.PerformanceCounterType.CounterMultiTimerInverse;
                        break;
                    case "counterMultiTimer100NsInverse":
                        type = System.Diagnostics.PerformanceCounterType.CounterMultiTimer100NsInverse;
                        break;
                    case "elapsedTime":
                        type = System.Diagnostics.PerformanceCounterType.ElapsedTime;
                        break;
                    case "sampleBase":
                        type = System.Diagnostics.PerformanceCounterType.SampleBase;
                        break;
                    case "rawBase":
                        type = System.Diagnostics.PerformanceCounterType.RawBase;
                        break;
                    case "counterMultiBase":
                        type = System.Diagnostics.PerformanceCounterType.CounterMultiBase;
                        break;
                    case "rateOfCountsPerSecond64":
                        type = System.Diagnostics.PerformanceCounterType.RateOfCountsPerSecond64;
                        break;
                    case "rateOfCountsPerSecond32":
                        type = System.Diagnostics.PerformanceCounterType.RateOfCountsPerSecond32;
                        break;
                    case "countPerTimeInterval64":
                        type = System.Diagnostics.PerformanceCounterType.CountPerTimeInterval64;
                        break;
                    case "countPerTimeInterval32":
                        type = System.Diagnostics.PerformanceCounterType.CountPerTimeInterval32;
                        break;
                    case "sampleCounter":
                        type = System.Diagnostics.PerformanceCounterType.SampleCounter;
                        break;
                    case "counterDelta64":
                        type = System.Diagnostics.PerformanceCounterType.CounterDelta64;
                        break;
                    case "numberOfItems64":
                        type = System.Diagnostics.PerformanceCounterType.NumberOfItems64;
                        break;
                    case "numberOfItems32":
                        type = System.Diagnostics.PerformanceCounterType.NumberOfItems32;
                        break;
                    case "numberOfItemsHEX64":
                        type = System.Diagnostics.PerformanceCounterType.NumberOfItemsHEX64;
                        break;
                    case "numberOfItemsHEX32":
                        type = System.Diagnostics.PerformanceCounterType.NumberOfItemsHEX32;
                        break;
                    default:
                        this.Messaging.Write(ErrorMessages.IllegalEmptyAttributeValue(sourceLineNumbers, attribute.Parent.Name.LocalName, attribute.Name.LocalName));
                        break;
                }
            }

            return type;
        }

        /// <summary>
        /// Parses a perf counter element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        /// <param name="fileId">Identifier of referenced file.</param>
        private void ParsePerfCounterElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId, string fileId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string name = null;

            this.Messaging.Write(UtilWarnings.DeprecatedPerfCounterElement(sourceLineNumbers));

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Name":
                            name = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == name)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Name"));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "Perfmon");
                row.Set(0, componentId);
                row.Set(1, $"[#{fileId}]");
                row.Set(2, name);
            }

            // Reference ConfigurePerfmonInstall and ConfigurePerfmonUninstall since nothing will happen without them
            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CAs are referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigurePerfmonInstall_ARM");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigurePerfmonUninstall_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigurePerfmonInstall");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigurePerfmonUninstall");
            }
        }


        /// <summary>
        /// Parses a perf manifest element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        /// <param name="fileId">Identifier of referenced file.</param>
        private void ParsePerfCounterManifestElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId, string fileId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string resourceFileDirectory = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "ResourceFileDirectory":
                            resourceFileDirectory = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "PerfmonManifest");
                row.Set(0, componentId);
                row.Set(1, $"[#{fileId}]");
                row.Set(2, resourceFileDirectory);
            }

            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CAs are referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigurePerfmonManifestRegister_ARM");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigurePerfmonManifestUnregister_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigurePerfmonManifestRegister");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigurePerfmonManifestUnregister");
            }
        }

        /// <summary>
        /// Parses a format files element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="fileId">Identifier of referenced file.</param>
        /// <param name="win64">Flag to determine whether the component is 64-bit.</param>
        private void ParseFormatFileElement(Intermediate intermediate, IntermediateSection section, XElement element, string fileId, bool win64)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string binaryId = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "BinaryKey":
                            binaryId = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (null == binaryId)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "BinaryKey"));
            }

            if (!this.Messaging.EncounteredError)
            {
                switch (this.Context.Platform)
                {
                    case Platform.X86:
                        this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixSchedFormatFiles");
                        break;
                    case Platform.X64:
                        this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixSchedFormatFiles_x64");
                        break;
                    case Platform.IA64:
                    case Platform.ARM:
                        this.Messaging.Write(ErrorMessages.UnsupportedPlatformForElement(sourceLineNumbers, this.Context.Platform.ToString(), element.Name.LocalName));
                        break;
                }

                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixFormatFiles");
                row.Set(0, binaryId);
                row.Set(1, fileId);

                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "Binary", binaryId);
            }
        }

        /// <summary>
        /// Parses a event manifest element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        /// <param name="fileId">Identifier of referenced file.</param>
        private void ParseEventManifestElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId, string fileId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string messageFile = null;
            string resourceFile = null;
            string parameterFile = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "MessageFile":
                            messageFile = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ResourceFile":
                            resourceFile = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ParameterFile":
                            parameterFile = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "EventManifest");
                row.Set(0, componentId);
                row.Set(1, $"[#{fileId}]");

                if (null != messageFile)
                {
                    var messageRow = this.ParseHelper.CreateRow(section, sourceLineNumbers, "XmlFile");
                    messageRow.Set(0, String.Concat("Config_", fileId, "MessageFile"));
                    messageRow.Set(1, $"[#{fileId}]");
                    messageRow.Set(2, "/*/*/*/*[\\[]@messageFileName[\\]]");
                    messageRow.Set(3, "messageFileName");
                    messageRow.Set(4, messageFile);
                    messageRow.Set(5, 4 | 0x00001000);  //bulk write | preserve modified date
                    messageRow.Set(6, componentId);
                }
                if (null != parameterFile)
                {
                    var resourceRow = this.ParseHelper.CreateRow(section, sourceLineNumbers, "XmlFile");
                    resourceRow.Set(0, String.Concat("Config_", fileId, "ParameterFile"));
                    resourceRow.Set(1, $"[#{fileId}]");
                    resourceRow.Set(2, "/*/*/*/*[\\[]@parameterFileName[\\]]");
                    resourceRow.Set(3, "parameterFileName");
                    resourceRow.Set(4, parameterFile);
                    resourceRow.Set(5, 4 | 0x00001000);  //bulk write | preserve modified date
                    resourceRow.Set(6, componentId);
                }
                if (null != resourceFile)
                {
                    var resourceRow = this.ParseHelper.CreateRow(section, sourceLineNumbers, "XmlFile");
                    resourceRow.Set(0, String.Concat("Config_", fileId, "ResourceFile"));
                    resourceRow.Set(1, $"[#{fileId}]");
                    resourceRow.Set(2, "/*/*/*/*[\\[]@resourceFileName[\\]]");
                    resourceRow.Set(3, "resourceFileName");
                    resourceRow.Set(4, resourceFile);
                    resourceRow.Set(5, 4 | 0x00001000);  //bulk write | preserve modified date
                    resourceRow.Set(6, componentId);
                }

            }

            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CA is referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureEventManifestRegister_ARM");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureEventManifestUnregister_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureEventManifestRegister");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureEventManifestUnregister");
            }

            if (null != messageFile || null != parameterFile || null != resourceFile)
            {
                if (this.Context.Platform == Platform.ARM)
                {
                    // Ensure ARM version of the CA is referenced
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedXmlFile_ARM");
                }
                else
                {
                    // All other supported platforms use x86
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedXmlFile");
                }
            }
        }

        /// <summary>
        /// Parses a PermissionEx element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="objectId">Identifier of object to be secured.</param>
        /// <param name="componentId">Identifier of component, used to determine install state.</param>
        /// <param name="win64">Flag to determine whether the component is 64-bit.</param>
        /// <param name="tableName">Name of table that contains objectId.</param>
        private void ParsePermissionExElement(Intermediate intermediate, IntermediateSection section, XElement element, string objectId, string componentId, bool win64, string tableName)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            BitArray bits = new BitArray(32);
            string domain = null;
            int permission = 0;
            string[] specialPermissions = null;
            string user = null;

            PermissionType permissionType = PermissionType.SecureObjects;

            switch (tableName)
            {
                case "CreateFolder":
                    specialPermissions = UtilConstants.FolderPermissions;
                    break;
                case "File":
                    specialPermissions = UtilConstants.FilePermissions;
                    break;
                case "Registry":
                    specialPermissions = UtilConstants.RegistryPermissions;
                    if (String.IsNullOrEmpty(objectId))
                    {
                        this.Messaging.Write(UtilErrors.InvalidRegistryObject(sourceLineNumbers, element.Parent.Name.LocalName));
                    }
                    break;
                case "ServiceInstall":
                    specialPermissions = UtilConstants.ServicePermissions;
                    permissionType = PermissionType.SecureObjects;
                    break;
                default:
                    this.ParseHelper.UnexpectedElement(element.Parent, element);
                    break;
            }

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Domain":
                            if (PermissionType.FileSharePermissions == permissionType)
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeWhenNested(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName, element.Parent.Name.LocalName));
                            }
                            domain = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "User":
                            user = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            YesNoType attribValue = this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            if (!this.TrySetBitFromName(UtilConstants.StandardPermissions, attrib.Name.LocalName, attribValue, bits, 16))
                            {
                                if (!this.TrySetBitFromName(UtilConstants.GenericPermissions, attrib.Name.LocalName, attribValue, bits, 28))
                                {
                                    if (!this.TrySetBitFromName(specialPermissions, attrib.Name.LocalName, attribValue, bits, 0))
                                    {
                                        this.ParseHelper.UnexpectedAttribute(element, attrib);
                                        break;
                                    }
                                }
                            }
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            permission = this.CreateIntegerFromBitArray(bits);

            if (null == user)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "User"));
            }

            if (int.MinValue == permission) // just GENERIC_READ, which is MSI_NULL
            {
                this.Messaging.Write(ErrorMessages.GenericReadNotAllowed(sourceLineNumbers));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                if (win64)
                {
                    if (this.Context.Platform == Platform.IA64)
                    {
                        this.Messaging.Write(ErrorMessages.UnsupportedPlatformForElement(sourceLineNumbers, "ia64", element.Name.LocalName));
                    }
                    else
                    {
                        // Ensure SchedSecureObjects (x64) is referenced
                        this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedSecureObjects_x64");
                    }
                }
                else if (this.Context.Platform == Platform.ARM)
                {
                    // Ensure SchedSecureObjects (arm) is referenced
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedSecureObjects_ARM");
                }
                else
                {
                    // Ensure SchedSecureObjects (x86) is referenced, to handle this x86 component member
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedSecureObjects");
                }

                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "SecureObjects");
                row.Set(0, objectId);
                row.Set(1, tableName);
                row.Set(2, domain);
                row.Set(3, user);
                row.Set(4, permission);
                row.Set(5, componentId);
            }
        }

        /// <summary>
        /// Parses a ProductSearch element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        private void ParseProductSearchElement(Intermediate intermediate, IntermediateSection section, XElement element)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            Identifier id = null;
            string variable = null;
            string condition = null;
            string after = null;
            string productCode = null;
            string upgradeCode = null;

            Serialize.ProductSearch.ResultType result = Serialize.ProductSearch.ResultType.NotSet;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                        case "Variable":
                        case "Condition":
                        case "After":
                            this.ParseCommonSearchAttributes(sourceLineNumbers, attrib, ref id, ref variable, ref condition, ref after);
                            break;
                        case "ProductCode":
                            productCode = this.ParseHelper.GetAttributeGuidValue(sourceLineNumbers, attrib, false);
                            break;
                        case "UpgradeCode":
                            upgradeCode = this.ParseHelper.GetAttributeGuidValue(sourceLineNumbers, attrib, false);
                            break;
                        case "Result":
                            string resultValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            if (!Serialize.ProductSearch.TryParseResultType(resultValue, out result))
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, attrib.Parent.Name.LocalName, attrib.Name.LocalName,
                                    resultValue,
                                    Serialize.ProductSearch.ResultType.version.ToString(),
                                    Serialize.ProductSearch.ResultType.language.ToString(),
                                    Serialize.ProductSearch.ResultType.state.ToString(),
                                    Serialize.ProductSearch.ResultType.assignment.ToString()));
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == variable)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Variable"));
            }

            if (null == upgradeCode && null == productCode)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "ProductCode", "UpgradeCode", true));
            }

            if (null != upgradeCode && null != productCode)
            {
                this.Messaging.Write(ErrorMessages.IllegalAttributeWithOtherAttribute(sourceLineNumbers, element.Name.LocalName, "UpgradeCode", "ProductCode"));
            }

            if (null == id)
            {
                id = this.ParseHelper.CreateIdentifier("wps", variable, condition, after, (productCode == null ? upgradeCode : productCode), result.ToString());
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                this.CreateWixSearchRow(section, sourceLineNumbers, id, variable, condition);
                if (after != null)
                {
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "WixSearch", after);
                    // TODO: We're currently defaulting to "always run after", which we will need to change...
                    this.CreateWixSearchRelationRow(section, sourceLineNumbers, id, after, 2);
                }

                WixProductSearchAttributes attributes = WixProductSearchAttributes.Version;
                switch (result)
                {
                    case Serialize.ProductSearch.ResultType.version:
                        attributes = WixProductSearchAttributes.Version;
                        break;
                    case Serialize.ProductSearch.ResultType.language:
                        attributes = WixProductSearchAttributes.Language;
                        break;
                    case Serialize.ProductSearch.ResultType.state:
                        attributes = WixProductSearchAttributes.State;
                        break;
                    case Serialize.ProductSearch.ResultType.assignment:
                        attributes = WixProductSearchAttributes.Assignment;
                        break;
                }

                // set an additional flag if this is an upgrade code
                if (null != upgradeCode)
                {
                    attributes |= WixProductSearchAttributes.UpgradeCode;
                }

                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixProductSearch", id);
                row.Set(1, productCode ?? upgradeCode);
                row.Set(2, (int)attributes);
            }
        }

        /// <summary>
        /// Parses a RegistrySearch element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        private void ParseRegistrySearchElement(Intermediate intermediate, IntermediateSection section, XElement element)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            Identifier id = null;
            string variable = null;
            string condition = null;
            string after = null;
            int root = CompilerConstants.IntegerNotSet;
            string key = null;
            string value = null;
            YesNoType expand = YesNoType.NotSet;
            YesNoType win64 = YesNoType.NotSet;
            Serialize.RegistrySearch.ResultType result = Serialize.RegistrySearch.ResultType.NotSet;
            Serialize.RegistrySearch.FormatType format = Serialize.RegistrySearch.FormatType.raw;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                        case "Variable":
                        case "Condition":
                        case "After":
                            this.ParseCommonSearchAttributes(sourceLineNumbers, attrib, ref id, ref variable, ref condition, ref after);
                            break;
                        case "Root":
                            root = this.ParseHelper.GetAttributeMsidbRegistryRootValue(sourceLineNumbers, attrib, false);
                            break;
                        case "Key":
                            key = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Value":
                            value = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ExpandEnvironmentVariables":
                            expand = this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            break;
                        case "Format":
                            string formatValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            if (!String.IsNullOrEmpty(formatValue))
                            {
                                if (!Serialize.RegistrySearch.TryParseFormatType(formatValue, out format))
                                {
                                    this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, attrib.Parent.Name.LocalName, attrib.Name.LocalName,
                                        formatValue, Serialize.RegistrySearch.FormatType.raw.ToString(), Serialize.RegistrySearch.FormatType.compatible.ToString()));
                                }
                            }
                            break;
                        case "Result":
                            string resultValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            if (!Serialize.RegistrySearch.TryParseResultType(resultValue, out result))
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, attrib.Parent.Name.LocalName, attrib.Name.LocalName,
                                    resultValue, Serialize.RegistrySearch.ResultType.exists.ToString(), Serialize.RegistrySearch.ResultType.value.ToString()));
                            }
                            break;
                        case "Win64":
                            win64 = this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == variable)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Variable"));
            }

            if (CompilerConstants.IntegerNotSet == root)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Root"));
            }

            if (null == key)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Key"));
            }

            if (Serialize.RegistrySearch.ResultType.NotSet == result)
            {
                result = Serialize.RegistrySearch.ResultType.value;
            }

            if (null == id)
            {
                id = this.ParseHelper.CreateIdentifier("wrs", variable, condition, after, root.ToString(), key, value, result.ToString());
            }

            WixRegistrySearchAttributes attributes = WixRegistrySearchAttributes.Raw;
            switch (format)
            {
                case Serialize.RegistrySearch.FormatType.raw:
                    attributes = WixRegistrySearchAttributes.Raw;
                    break;
                case Serialize.RegistrySearch.FormatType.compatible:
                    attributes = WixRegistrySearchAttributes.Compatible;
                    break;
            }

            switch (result)
            {
                case Serialize.RegistrySearch.ResultType.exists:
                    attributes |= WixRegistrySearchAttributes.WantExists;
                    break;
                case Serialize.RegistrySearch.ResultType.value:
                    attributes |= WixRegistrySearchAttributes.WantValue;
                    break;
            }

            if (expand == YesNoType.Yes)
            {
                if (0 != (attributes & WixRegistrySearchAttributes.WantExists))
                {
                    this.Messaging.Write(ErrorMessages.IllegalAttributeValueWithOtherAttribute(sourceLineNumbers, element.Name.LocalName,
                        "ExpandEnvironmentVariables", expand.ToString(), "Result", result.ToString()));
                }

                attributes |= WixRegistrySearchAttributes.ExpandEnvironmentVariables;
            }

            if (win64 == YesNoType.Yes)
            {
                attributes |= WixRegistrySearchAttributes.Win64;
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                this.CreateWixSearchRow(section, sourceLineNumbers, id, variable, condition);
                if (after != null)
                {
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "WixSearch", after);
                    // TODO: We're currently defaulting to "always run after", which we will need to change...
                    this.CreateWixSearchRelationRow(section, sourceLineNumbers, id, after, 2);
                }

                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixRegistrySearch", id);
                row.Set(1, root);
                row.Set(2, key);
                row.Set(3, value);
                row.Set(4, (int)attributes);
            }
        }

        /// <summary>
        /// Parses a RemoveFolderEx element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        private void ParseRemoveFolderExElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            Identifier id = null;
            int on = (int)WixRemoveFolderExOn.Uninstall;
            string property = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifier(sourceLineNumbers, attrib);
                            break;
                        case "On":
                            string onValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            if (onValue.Length == 0)
                            {
                                on = CompilerConstants.IllegalInteger;
                            }
                            else
                            {
                                switch (onValue)
                                {
                                    case "install":
                                        on = (int)WixRemoveFolderExOn.Install;
                                        break;
                                    case "uninstall":
                                        on = (int)WixRemoveFolderExOn.Uninstall;
                                        break;
                                    case "both":
                                        on = (int)WixRemoveFolderExOn.Both;
                                        break;
                                    default:
                                        this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, element.Name.LocalName, "On", onValue, "install", "uninstall", "both"));
                                        on = CompilerConstants.IllegalInteger;
                                        break;
                                }
                            }
                            break;
                        case "Property":
                            property = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (String.IsNullOrEmpty(property))
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Property"));
            }

            if (null == id)
            {
                id = this.ParseHelper.CreateIdentifier("wrf", componentId, property, on.ToString(CultureInfo.InvariantCulture.NumberFormat));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixRemoveFolderEx", id);
                row.Set(1, componentId);
                row.Set(2, property);
                row.Set(3, on);

                this.ParseHelper.EnsureTable(section, sourceLineNumbers, "RemoveFile");
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixRemoveFoldersEx");
            }
        }

        /// <summary>
        /// Parses a RestartResource element.
        /// </summary>
        /// <param name="element">The element to parse.</param>
        /// <param name="componentId">The identity of the parent component.</param>
        private void ParseRestartResourceElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            Identifier id = null;
            string resource = null;
            int attributes = CompilerConstants.IntegerNotSet;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifier(sourceLineNumbers, attrib);
                            break;

                        case "Path":
                            resource = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            attributes = (int)WixRestartResourceAttributes.Filename;
                            break;

                        case "ProcessName":
                            resource = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            attributes = (int)WixRestartResourceAttributes.ProcessName;
                            break;

                        case "ServiceName":
                            resource = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            attributes = (int)WixRestartResourceAttributes.ServiceName;
                            break;

                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            // Validate the attribute.
            if (null == id)
            {
                id = this.ParseHelper.CreateIdentifier("wrr", componentId, resource, attributes.ToString());
            }

            if (String.IsNullOrEmpty(resource) || CompilerConstants.IntegerNotSet == attributes)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttributes(sourceLineNumbers, element.Name.LocalName, "Path", "ServiceName"));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                // Add a reference to the WixRegisterRestartResources custom action since nothing will happen without it.
                if (this.Context.Platform == Platform.ARM)
                {
                    // Ensure ARM version of the CA is referenced
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixRegisterRestartResources_ARM");
                }
                else
                {
                    // All other supported platforms use x86
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixRegisterRestartResources");
                }

                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixRestartResource", id);
                row.Set(1, componentId);
                row.Set(2, resource);
                row.Set(3, attributes);
            }
        }

        /// <summary>
        /// Parses a service configuration element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        /// <param name="parentTableName">Name of parent element.</param>
        /// <param name="parentTableServiceName">Optional name of service </param>
        private void ParseServiceConfigElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId, string parentTableName, string parentTableServiceName)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string firstFailureActionType = null;
            bool newService = false;
            string programCommandLine = null;
            string rebootMessage = null;
            int resetPeriod = CompilerConstants.IntegerNotSet;
            int restartServiceDelay = CompilerConstants.IntegerNotSet;
            string secondFailureActionType = null;
            string serviceName = null;
            string thirdFailureActionType = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "FirstFailureActionType":
                            firstFailureActionType = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ProgramCommandLine":
                            programCommandLine = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "RebootMessage":
                            rebootMessage = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ResetPeriodInDays":
                            resetPeriod = this.ParseHelper.GetAttributeIntegerValue(sourceLineNumbers, attrib, 0, int.MaxValue);
                            break;
                        case "RestartServiceDelayInSeconds":
                            restartServiceDelay = this.ParseHelper.GetAttributeIntegerValue(sourceLineNumbers, attrib, 0, int.MaxValue);
                            break;
                        case "SecondFailureActionType":
                            secondFailureActionType = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ServiceName":
                            serviceName = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ThirdFailureActionType":
                            thirdFailureActionType = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            // if this element is a child of ServiceInstall then ignore the service name provided.
            if ("ServiceInstall" == parentTableName)
            {
                // TODO: the ServiceName attribute should not be allowed in this case (the overwriting behavior may confuse users)
                serviceName = parentTableServiceName;
                newService = true;
            }
            else
            {
                // not a child of ServiceInstall, so ServiceName must have been provided
                if (null == serviceName)
                {
                    this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "ServiceName"));
                }
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            // Reference SchedServiceConfig since nothing will happen without it
            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CA is referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedServiceConfig_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedServiceConfig");
            }

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "ServiceConfig");
                row.Set(0, serviceName);
                row.Set(1, componentId);
                row.Set(2, (newService ? 1 : 0));
                row.Set(3, firstFailureActionType);
                row.Set(4, secondFailureActionType);
                row.Set(5, thirdFailureActionType);
                if (CompilerConstants.IntegerNotSet != resetPeriod)
                {
                    row.Set(6, resetPeriod);
                }

                if (CompilerConstants.IntegerNotSet != restartServiceDelay)
                {
                    row.Set(7, restartServiceDelay);
                }
                row.Set(8, programCommandLine);
                row.Set(9, rebootMessage);
            }
        }

        /// <summary>
        /// Parses a touch file element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        /// <param name="win64">Indicates whether the path is a 64-bit path.</param>
        private void ParseTouchFileElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId, bool win64)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            Identifier id = null;
            string path = null;
            YesNoType onInstall = YesNoType.NotSet;
            YesNoType onReinstall = YesNoType.NotSet;
            YesNoType onUninstall = YesNoType.NotSet;
            YesNoType nonvital = YesNoType.NotSet;
            int attributes = 0;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifier(sourceLineNumbers, attrib);
                            break;
                        case "Path":
                            path = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "OnInstall":
                            onInstall = this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            break;
                        case "OnReinstall":
                            onReinstall = this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            break;
                        case "OnUninstall":
                            onUninstall = this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            break;
                        case "Nonvital":
                            nonvital = this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == path)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Path"));
            }

            // If none of the scheduling actions are set, default to touching on install and reinstall.
            if (YesNoType.NotSet == onInstall && YesNoType.NotSet == onReinstall && YesNoType.NotSet == onUninstall)
            {
                onInstall = YesNoType.Yes;
                onReinstall = YesNoType.Yes;
            }

            attributes |= YesNoType.Yes == onInstall ? 0x1 : 0;
            attributes |= YesNoType.Yes == onReinstall ? 0x2 : 0;
            attributes |= YesNoType.Yes == onUninstall ? 0x4 : 0;
            attributes |= win64 ? 0x10 : 0;
            attributes |= YesNoType.Yes == nonvital ? 0 : 0x20;

            if (null == id)
            {
                id = this.ParseHelper.CreateIdentifier("tf", path, attributes.ToString());
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "WixTouchFile", id);
                row.Set(1, componentId);
                row.Set(2, path);
                row.Set(3, attributes);

                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "WixTouchFileDuringInstall");
            }
        }

        /// <summary>
        /// Parses an user element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Optional identifier of parent component.</param>
        private void ParseUserElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            Identifier id = null;
            int attributes = 0;
            string domain = null;
            string name = null;
            string password = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifier(sourceLineNumbers, attrib);
                            break;
                        case "CanNotChangePassword":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }

                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserPasswdCantChange;
                            }
                            break;
                        case "CreateUser":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }

                            if (YesNoType.No == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserDontCreateUser;
                            }
                            break;
                        case "Disabled":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }

                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserDisableAccount;
                            }
                            break;
                        case "Domain":
                            domain = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "FailIfExists":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }

                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserFailIfExists;
                            }
                            break;
                        case "LogonAsService":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserLogonAsService;
                            }
                            break;
                        case "LogonAsBatchJob":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserLogonAsBatchJob;
                            }
                            break;
                        case "Name":
                            name = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Password":
                            password = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "PasswordExpired":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }

                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserPasswdChangeReqdOnLogin;
                            }
                            break;
                        case "PasswordNeverExpires":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }

                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserDontExpirePasswrd;
                            }
                            break;
                        case "RemoveOnUninstall":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }

                            if (YesNoType.No == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserDontRemoveOnUninstall;
                            }
                            break;
                        case "UpdateIfExists":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }

                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserUpdateIfExists;
                            }
                            break;
                        case "Vital":
                            if (null == componentId)
                            {
                                this.Messaging.Write(UtilErrors.IllegalAttributeWithoutComponent(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName));
                            }

                            if (YesNoType.No == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                attributes |= UserNonVital;
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == id)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Id"));
            }

            if (null == name)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Name"));
            }

            foreach (XElement child in element.Elements())
            {
                if (this.Namespace == child.Name.Namespace)
                {
                    switch (child.Name.LocalName)
                    {
                        case "GroupRef":
                            if (null == componentId)
                            {
                                SourceLineNumber childSourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(child);
                                this.Messaging.Write(UtilErrors.IllegalElementWithoutComponent(childSourceLineNumbers, child.Name.LocalName));
                            }

                            this.ParseGroupRefElement(intermediate, section, child, id.Id);
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(element, child);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionElement(this.Context.Extensions, intermediate, section, element, child);
                }
            }

            if (null != componentId)
            {
                // Reference ConfigureIIs since nothing will happen without it
                if (this.Context.Platform == Platform.ARM)
                {
                    // Ensure ARM version of the CA is referenced
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureUsers_ARM");
                }
                else
                {
                    // All other supported platforms use x86
                    this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "ConfigureUsers");
                }
            }

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "User", id);
                row.Set(1, componentId);
                row.Set(2, name);
                row.Set(3, domain);
                row.Set(4, password);
                row.Set(5, attributes);
            }
        }

        /// <summary>
        /// Parses a XmlFile element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        private void ParseXmlFileElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string id = null;
            string file = null;
            string elementPath = null;
            string name = null;
            string value = null;
            int sequence = -1;
            int flags = 0;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Action":
                            string actionValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            switch (actionValue)
                            {
                                case "createElement":
                                    flags |= 0x00000001; // XMLFILE_CREATE_ELEMENT
                                    break;
                                case "deleteValue":
                                    flags |= 0x00000002; // XMLFILE_DELETE_VALUE
                                    break;
                                case "bulkSetValue":
                                    flags |= 0x00000004; // XMLFILE_BULKWRITE_VALUE
                                    break;
                                case "setValue":
                                    // no flag for set value since it's the default
                                    break;
                                default:
                                    this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, element.Name.LocalName, "Action", actionValue, "createElement", "deleteValue", "setValue", "bulkSetValue"));
                                    break;
                            }
                            break;
                        case "SelectionLanguage":
                            string selectionLanguage = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            switch (selectionLanguage)
                            {
                                case "XPath":
                                    flags |= 0x00000100; // XMLFILE_USE_XPATH
                                    break;
                                case "XSLPattern":
                                    // no flag for since it's the default
                                    break;
                                default:
                                    this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, element.Name.LocalName, "SelectionLanguage", selectionLanguage, "XPath", "XSLPattern"));
                                    break;
                            }
                            break;
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            break;
                        case "File":
                            file = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ElementPath":
                            elementPath = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Name":
                            name = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Permanent":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                flags |= 0x00010000; // XMLFILE_DONT_UNINSTALL
                            }
                            break;
                        case "Sequence":
                            sequence = this.ParseHelper.GetAttributeIntegerValue(sourceLineNumbers, attrib, 1, short.MaxValue);
                            break;
                        case "Value":
                            value = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "PreserveModifiedDate":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                flags |= 0x00001000; // XMLFILE_PRESERVE_MODIFIED
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == id)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Id"));
            }

            if (null == file)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "File"));
            }

            if (null == elementPath)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "ElementPath"));
            }

            if ((0x00000001 /*XMLFILE_CREATE_ELEMENT*/ & flags) != 0 && null == name)
            {
                this.Messaging.Write(ErrorMessages.IllegalAttributeWithoutOtherAttributes(sourceLineNumbers, element.Name.LocalName, "Action", "Name"));
            }

            this.ParseHelper.ParseForExtensionElements(this.Context.Extensions, intermediate, section, element);

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "XmlFile");
                row.Set(0, id);
                row.Set(1, file);
                row.Set(2, elementPath);
                row.Set(3, name);
                row.Set(4, value);
                row.Set(5, flags);
                row.Set(6, componentId);
                if (-1 != sequence)
                {
                    row.Set(7, sequence);
                }
            }

            // Reference SchedXmlFile since nothing will happen without it
            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CA is referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedXmlFile_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedXmlFile");
            }
        }

        /// <summary>
        /// Parses a XmlConfig element.
        /// </summary>
        /// <param name="element">Element to parse.</param>
        /// <param name="componentId">Identifier of parent component.</param>
        /// <param name="nested">Whether or not the element is nested.</param>
        private void ParseXmlConfigElement(Intermediate intermediate, IntermediateSection section, XElement element, string componentId, bool nested)
        {
            SourceLineNumber sourceLineNumbers = this.ParseHelper.GetSourceLineNumbers(element);
            string id = null;
            string elementId = null;
            string elementPath = null;
            int flags = 0;
            string file = null;
            string name = null;
            int sequence = CompilerConstants.IntegerNotSet;
            string value = null;
            string verifyPath = null;

            foreach (XAttribute attrib in element.Attributes())
            {
                if (String.IsNullOrEmpty(attrib.Name.NamespaceName) || this.Namespace == attrib.Name.Namespace)
                {
                    switch (attrib.Name.LocalName)
                    {
                        case "Id":
                            id = this.ParseHelper.GetAttributeIdentifierValue(sourceLineNumbers, attrib);
                            break;
                        case "Action":
                            if (nested)
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeWhenNested(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName, element.Parent.Name.LocalName));
                            }
                            else
                            {
                                string actionValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                                switch (actionValue)
                                {
                                    case "create":
                                        flags |= 0x10; // XMLCONFIG_CREATE
                                        break;
                                    case "delete":
                                        flags |= 0x20; // XMLCONFIG_DELETE
                                        break;
                                    default:
                                        this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName, actionValue, "create", "delete"));
                                        break;
                                }
                            }
                            break;
                        case "ElementId":
                            elementId = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "ElementPath":
                            elementPath = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "File":
                            file = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Name":
                            name = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "Node":
                            if (nested)
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeWhenNested(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName, element.Parent.Name.LocalName));
                            }
                            else
                            {
                                string nodeValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                                switch (nodeValue)
                                {
                                    case "element":
                                        flags |= 0x1; // XMLCONFIG_ELEMENT
                                        break;
                                    case "value":
                                        flags |= 0x2; // XMLCONFIG_VALUE
                                        break;
                                    case "document":
                                        flags |= 0x4; // XMLCONFIG_DOCUMENT
                                        break;
                                    default:
                                        this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName, nodeValue, "element", "value", "document"));
                                        break;
                                }
                            }
                            break;
                        case "On":
                            if (nested)
                            {
                                this.Messaging.Write(ErrorMessages.IllegalAttributeWhenNested(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName, element.Parent.Name.LocalName));
                            }
                            else
                            {
                                string onValue = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                                switch (onValue)
                                {
                                    case "install":
                                        flags |= 0x100; // XMLCONFIG_INSTALL
                                        break;
                                    case "uninstall":
                                        flags |= 0x200; // XMLCONFIG_UNINSTALL
                                        break;
                                    default:
                                        this.Messaging.Write(ErrorMessages.IllegalAttributeValue(sourceLineNumbers, element.Name.LocalName, attrib.Name.LocalName, onValue, "install", "uninstall"));
                                        break;
                                }
                            }
                            break;
                        case "PreserveModifiedDate":
                            if (YesNoType.Yes == this.ParseHelper.GetAttributeYesNoValue(sourceLineNumbers, attrib))
                            {
                                flags |= 0x00001000; // XMLCONFIG_PRESERVE_MODIFIED
                            }
                            break;
                        case "Sequence":
                            sequence = this.ParseHelper.GetAttributeIntegerValue(sourceLineNumbers, attrib, 1, short.MaxValue);
                            break;
                        case "Value":
                            value = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        case "VerifyPath":
                            verifyPath = this.ParseHelper.GetAttributeValue(sourceLineNumbers, attrib);
                            break;
                        default:
                            this.ParseHelper.UnexpectedAttribute(element, attrib);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionAttribute(this.Context.Extensions, intermediate, section, element, attrib);
                }
            }

            if (null == id)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "Id"));
            }

            if (null == file)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttribute(sourceLineNumbers, element.Name.LocalName, "File"));
            }

            if (null == elementId && null == elementPath)
            {
                this.Messaging.Write(ErrorMessages.ExpectedAttributes(sourceLineNumbers, element.Name.LocalName, "ElementId", "ElementPath"));
            }
            else if (null != elementId)
            {
                if (null != elementPath)
                {
                    this.Messaging.Write(ErrorMessages.IllegalAttributeWithOtherAttribute(sourceLineNumbers, element.Name.LocalName, "ElementId", "ElementPath"));
                }

                if (0 != flags)
                {
                    this.Messaging.Write(ErrorMessages.IllegalAttributeWithOtherAttributes(sourceLineNumbers, element.Name.LocalName, "ElementId", "Action", "Node", "On"));
                }

                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "XmlConfig", elementId);
            }

            string innerText = this.ParseHelper.GetTrimmedInnerText(element);
            if (null != value)
            {
                // cannot specify both the value attribute and inner text
                if (!String.IsNullOrEmpty(innerText))
                {
                    this.Messaging.Write(ErrorMessages.IllegalAttributeWithInnerText(sourceLineNumbers, element.Name.LocalName, "Value"));
                }
            }
            else // value attribute not specified
            {
                if (!String.IsNullOrEmpty(innerText))
                {
                    value = innerText;
                }
            }

            // find unexpected child elements
            foreach (XElement child in element.Elements())
            {
                if (this.Namespace == child.Name.Namespace)
                {
                    switch (child.Name.LocalName)
                    {
                        case "XmlConfig":
                            if (nested)
                            {
                                this.Messaging.Write(ErrorMessages.UnexpectedElement(sourceLineNumbers, element.Name.LocalName, child.Name.LocalName));
                            }
                            else
                            {
                                this.ParseXmlConfigElement(intermediate, section, child, componentId, true);
                            }
                            break;
                        default:
                            this.ParseHelper.UnexpectedElement(element, child);
                            break;
                    }
                }
                else
                {
                    this.ParseHelper.ParseExtensionElement(this.Context.Extensions, intermediate, section, element, child);
                }
            }

            if (!this.Messaging.EncounteredError)
            {
                var row = this.ParseHelper.CreateRow(section, sourceLineNumbers, "XmlConfig");
                row.Set(0, id);
                row.Set(1, file);
                row.Set(2, elementId ?? elementPath);
                row.Set(3, verifyPath);
                row.Set(4, name);
                row.Set(5, value);
                row.Set(6, flags);
                row.Set(7, componentId);
                if (CompilerConstants.IntegerNotSet != sequence)
                {
                    row.Set(8, sequence);
                }
            }

            // Reference SchedXmlConfig since nothing will happen without it
            if (this.Context.Platform == Platform.ARM)
            {
                // Ensure ARM version of the CA is referenced
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedXmlConfig_ARM");
            }
            else
            {
                // All other supported platforms use x86
                this.ParseHelper.CreateSimpleReference(section, sourceLineNumbers, "CustomAction", "SchedXmlConfig");
            }
        }

        /// <summary>
        /// Match evaluator to escape properties in a string.
        /// </summary>
        private string EscapeProperties(Match match)
        {
            string escape = null;
            switch (match.Value)
            {
                case "[":
                    escape = @"[\[]";
                    break;
                case "]":
                    escape = @"[\]]";
                    break;
            }

            return escape;
        }

        private int CreateIntegerFromBitArray(BitArray bits)
        {
            if (32 != bits.Length)
            {
                throw new ArgumentException(String.Format("Can only convert a bit array with 32-bits to integer. Actual number of bits in array: {0}", bits.Length), "bits");
            }

            int[] intArray = new int[1];
            bits.CopyTo(intArray, 0);

            return intArray[0];
        }

        private bool TrySetBitFromName(string[] attributeNames, string attributeName, YesNoType attributeValue, BitArray bits, int offset)
        {
            for (int i = 0; i < attributeNames.Length; i++)
            {
                if (attributeName.Equals(attributeNames[i], StringComparison.Ordinal))
                {
                    bits.Set(i + offset, YesNoType.Yes == attributeValue);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Private class that stores the data from a parsed PerformanceCounter element.
        /// </summary>
        private class ParsedPerformanceCounter
        {
            string name;
            string help;
            int type;
            string language;

            internal ParsedPerformanceCounter(string name, string help, System.Diagnostics.PerformanceCounterType type, int language)
            {
                this.name = name;
                this.help = help;
                this.type = (int)type;
                this.language = language.ToString("D3", CultureInfo.InvariantCulture);
            }

            internal string Name
            {
                get { return this.name; }
            }

            internal string Help
            {
                get { return this.help; }
            }

            internal int Type
            {
                get { return this.type; }
            }

            internal string Language
            {
                get { return this.language; }
            }
        }
    }
}
