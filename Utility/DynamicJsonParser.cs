//
//  Copyright (c) Microsoft Corporation. All rights reserved.
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//

namespace Microsoft.PackageManagement.NuGetProvider
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Dynamic;
    using System.Linq;
    using System.Management.Automation;
    using System.Reflection;
    using System.Xml;
    using System.IO;
    using Resources;

    /// <summary>
    /// Using PowerShell instance, parse valid JSON into a dynamic object.
    /// </summary>
    public class DynamicJsonParser
    {
        private static object psCreateLock = new object();
        private static System.Management.Automation.PowerShell powershell = null;

        private static System.Management.Automation.PowerShell PowerShellInstance
        {
            get
            {
                if (powershell == null)
                {
                    lock (psCreateLock)
                    {
                        if (powershell == null)
                        {
                            powershell = Init();
                        }
                    }
                }

                return powershell;
            }
        }

        private static System.Management.Automation.PowerShell Init()
        {
            return System.Management.Automation.PowerShell.Create();
        }

        private DynamicJsonParser() { }

        static DynamicJsonParser() { }

        /// <summary>
        /// Parse valid JSON into a dynamic object. All properties starting with @ or $ are added to the Metadata property object.
        /// </summary>
        /// <param name="json">JSON to parse.</param>
        /// <param name="numericalPrefix">Prefix for properties that start with a number.</param>
        /// <param name="lowercaseProperties">Lowercase all properties when you don't know the casing of the input JSON.</param>
        /// <returns>Dynamic object.</returns>
        public static dynamic Parse(string json, string numericalPrefix = "n", bool lowercaseProperties = true)
        {
            PSObject result;

            lock (PowerShellInstance)
            {
                PowerShellInstance.Commands.Clear();
                PowerShellInstance.AddCommand("ConvertFrom-Json").AddParameter("InputObject", json);
                result = PowerShellInstance.Invoke().FirstOrDefault();
            }

            return GetDynamic(result);
        }

        /// <summary>
        /// Convert dynamic object back into JSON.
        /// </summary>
        /// <param name="obj">dynamic object parsed using DynamicJsonParser.</param>
        /// <param name="numericalPrefix">Numerical prefix of properties beginning with digit.</param>
        /// <returns>JSON</returns>
        public static string Serialize(dynamic obj, string numericalPrefix = "n")
        {
            PSObject result;
            int flattenDepth = 0;
            Dictionary<string, object> res = FlattenDynamic(obj, numericalPrefix, 0, ref flattenDepth);
            lock (PowerShellInstance)
            {
                PowerShellInstance.Commands.Clear();
                PowerShellInstance.AddCommand("ConvertTo-Json").AddParameter("InputObject", res).AddParameter("Compress", true).AddParameter("Depth", flattenDepth);
                result = PowerShellInstance.Invoke().FirstOrDefault();
            }

            return result.ImmediateBaseObject as string;
        }

        private static dynamic GetDynamic(PSObject obj, string numericalPrefix = "n", bool lowercaseProperties = true)
        {
            dynamic res = new ExpandoObject();
            res.Metadata = new ExpandoObject();
            res.HasProperty = (Func<string, bool>)((string propertyName) =>
            {
                return ((IDictionary<string, object>)res).ContainsKey(propertyName);
            });
            res.Metadata.HasProperty = (Func<string, bool>)((string propertyName) =>
            {
                return ((IDictionary<string, object>)res.Metadata).ContainsKey(propertyName);
            });

            foreach (var psProperty in obj.Properties)
            {
                string propertyName = psProperty.Name;
                dynamic actualObj = res;
                // For now, treat "@" as metadata as well
                if (propertyName.StartsWith("@") || propertyName.StartsWith("$"))
                {
                    actualObj = res.Metadata;
                    propertyName = propertyName.Substring(1);
                }

                if (Char.IsNumber(propertyName[0]))
                {
                    propertyName = numericalPrefix + propertyName;
                }

                if (lowercaseProperties)
                {
                    propertyName = propertyName.ToLowerInvariant();
                }

                // Nuspec contains xml metadata as a string (only applicable to VSTS feeds)
                // Example nuspec property:
                // "nuspec":"<?xml version=\"1.0\" encoding=\"utf-8\"?>
                //           <package xmlns=\"http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd\">
                //                <metadata>
                //                      <id>Microsoft.Kusto.Tools</id>
                //                      <version>1.0.6</version>
                //                      <title>Kusto Tools</title>
                //                      <authors>Microsoft</authors>
                //                      <owners>Microsoft</owners>
                //                      <requireLicenseAcceptance>false</requireLicenseAcceptance>
                //                      <licenseUrl>http://kusto.blob.core.windows.net/kusto-nuget/EULA-agreement.htm</licenseUrl>
                //                      <projectUrl>http://aka.ms/kdocs</projectUrl>
                //                      <iconUrl>http://go.microsoft.com/fwlink/?LinkID=288890</iconUrl>
                //                      <description>Kusto Tools</description>
                //                      <releaseNotes>1.0.6: LightIngest refactored</releaseNotes>
                //                      <copyright>Copyright ©  Microsoft Corporation</copyright>
                //              </metadata>
                //          </package>"
                if (string.Equals(propertyName, "nuspec", StringComparison.OrdinalIgnoreCase))
                {
                    string xmlString = (string)psProperty.Value;

                    if (string.IsNullOrWhiteSpace(xmlString))
                    {
                         var message = string.Format(Messages.InvalidNuspec, "xmlString");
                         throw new InvalidDataException(message);
                    }

                    // Remove any characters that may come before the xml tag
                    // Characters may appear before the start of an xml tag if xml encoding is specified
                    if ((xmlString[0] != '<') && xmlString.Contains('<'))
                    {
                        var strIndex = xmlString.IndexOf('<');
                        xmlString = xmlString.Substring(strIndex, xmlString.Length - 1);
                    }
                    else
                    {
                        var message = string.Format(Messages.InvalidNuspec, "xmlString");
                        throw new InvalidDataException(message);
                    }

                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xmlString);
                    XmlNodeList metadataNodeList = xmlDoc.GetElementsByTagName("metadata");

                    foreach (XmlNode node in metadataNodeList)
                    {
                        if (node.HasChildNodes)
                        {
                            // Adding VSTS feed metadata values to the object containing nupkg property information
                            for (int i = 0; i < node.ChildNodes.Count; i++)
                            {
                                string property = node.ChildNodes[i].Name;
                                string propertyVal = node.ChildNodes[i].InnerText;

                                // Check if the property already exists in the object so we don't overwrite it
                                if (!((IDictionary<string, object>)actualObj).ContainsKey(property))
                                {
                                    object actualPropertyVal = ConvertObject(propertyVal);
                                    ((IDictionary<string, object>)actualObj)[property] = actualPropertyVal;
                                }
                            }
                        }
                    }
                }

                object actualVal = ConvertObject(psProperty.Value);
                ((IDictionary<string, object>)actualObj)[propertyName] = actualVal;
            }

            return res;
        }

        private static object ConvertObject(object o)
        {
            if (o != null && o is PSObject)
            {
                return GetDynamic((PSObject)o);
            }
            else if (o != null && o.GetType().IsArray)
            {
                ArrayList array = new ArrayList(((Array)o).Length);
                foreach (object oc in (Array)o)
                {
                    array.Add(ConvertObject(oc));
                }

                return array;
            }
            else
            {
                // Use the raw object - in the case of objects that weren't represented by a PSObject, which is a primitive-like type, or null
                return o;
            }
        }

        private static Dictionary<string, object> FlattenDynamic(dynamic obj, string numericalPrefix, int currentFlattenDepth, ref int maxFlattenDepth)
        {
            FlattenDepthCheck(currentFlattenDepth, ref maxFlattenDepth);
            Dictionary<string, object> act = new Dictionary<string, object>();
            IDictionary<string, object> d = (IDictionary<string, object>)obj;
            foreach (string key in d.Keys)
            {
                if (key.Equals("Metadata"))
                {
                    Dictionary<string, object> metadataFlattened = FlattenDynamic(d[key] as dynamic, numericalPrefix, currentFlattenDepth + 1, ref maxFlattenDepth);
                    foreach (string mKey in metadataFlattened.Keys)
                    {
                        act["@" + mKey] = metadataFlattened[mKey];
                    }
                }
                else
                {
                    string actualKey = key;
                    // Check if this is a numerical property
                    if (actualKey.StartsWith(numericalPrefix) && actualKey.Length > 1 && Char.IsDigit(actualKey[1]))
                    {
                        actualKey = actualKey.Substring(1);
                    }
                    object converted = FlattenObject(d[key], numericalPrefix, currentFlattenDepth + 1, ref maxFlattenDepth);
                    if (converted != null)
                    {
                        act[actualKey] = converted;
                    }
                }
            }

            return act;
        }

        private static void FlattenDepthCheck(int currentFlattenDepth, ref int maxFlattenDepth)
        {
            if (maxFlattenDepth < currentFlattenDepth)
            {
                maxFlattenDepth = currentFlattenDepth;
            }
        }

        private static object FlattenObject(object o, string numericalPrefix, int currentFlattenDepth, ref int maxFlattenDepth)
        {
            FlattenDepthCheck(currentFlattenDepth, ref maxFlattenDepth);
            if (o is ExpandoObject)
            {
                return FlattenDynamic(o as dynamic, numericalPrefix, currentFlattenDepth + 1, ref maxFlattenDepth);
            }
            else if (o.GetType().GetTypeInfo().IsGenericType && o.GetType().GetGenericTypeDefinition().GetTypeInfo().BaseType == typeof(MulticastDelegate))
            {
                return null;
            }
            else if (o is ArrayList)
            {
                ArrayList newList = new ArrayList(((ArrayList)o).Count);
                foreach (object item in (ArrayList)o)
                {
                    newList.Add(FlattenObject(item, numericalPrefix, currentFlattenDepth + 1, ref maxFlattenDepth));
                }

                return newList.ToArray();
            }
            else
            {
                return o;
            }
        }
    }
}
