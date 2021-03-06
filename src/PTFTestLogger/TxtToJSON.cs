﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;

namespace Microsoft.Protocols.TestTools
{
    /// <summary>
    /// Translates the data from txt to js as json, adds category to the test cases
    /// </summary>
    public class TxtToJSON
    {
        // Auxiliary variable to save information as JSON
        // Initialize the maximum length of JSON output to 32M
        /// <summary>
        /// Translates List<DataType.TestCase> to DataType.TestCasesSummary string
        /// </summary>
        /// <param name="results">The dictionary containing all the test case results</param>
        /// <returns>Returns detailed test cases information</returns>
        public string TestCasesString(ConcurrentDictionary<string, DataType.TestCaseDetail> results)
        {
            List<DataType.TestCase> testCaseList = GetTestCaseList(results);
            DataType.TestCasesSummary rs = new DataType.TestCasesSummary()
            {
                TestCases = testCaseList,
                TestCasesCategories = GetTestCaseCategortyList(testCaseList),
                TestCasesClasses = GetTestCaseClassList(testCaseList)
            };

            return Serialize(rs);
        }

        /// <summary>
        /// Constructs details of test case.
        /// 1. Gets log from the test case detail
        /// 2. Copies capture to the log folder, and saves the path.
        /// </summary>
        /// <param name="caseDetail">The informatoin of each test case which contains the detail log</param>
        /// <param name="captureFolder">Capture folder path</param>
        /// <returns>return the log information in Json format</returns>
        public string ConstructCaseDetail(DataType.TestCaseDetail caseDetail, string captureFolder)
        {
            caseDetail.CapturePath = CopyCaptureAndReturnPath(caseDetail.FullyQualifiedName, captureFolder);

            var caseDetailForJson = new DataType.TestCaseDetailForJson();

            caseDetailForJson.Update(caseDetail);

            return Serialize(caseDetailForJson);
        }

        /// <summary>
        /// Gets the test case list with basic information
        /// </summary>
        /// <param name="resultFolder">The path to the result folder</param>
        /// <param name="captureFolder">The path to the capture folder</param>
        /// <param name="results">The dictionary containing all the test case results</param>
        /// <returns>Returns the test case list with basic information</returns>
        private List<DataType.TestCase> GetTestCaseList(ConcurrentDictionary<string, DataType.TestCaseDetail> results)
        {
            Dictionary<string, DataType.TestCase> testCases = new Dictionary<string, DataType.TestCase>();

            foreach (var kvp in results)
            {
                DataType.TestCase tc = new DataType.TestCase()
                {
                    FullyQualifiedName = kvp.Key,
                    Name = kvp.Value.Name,
                    Result = kvp.Value.Result,
                    ClassType = kvp.Value.ClassType,
                    Category = kvp.Value.Categories,
                };
                testCases[kvp.Key] = tc; // overwrite the same case if any
            }

            List<DataType.TestCase> testCaseList = testCases.Values.OrderBy(tc => tc.Name).ToList(); // Sort by case name
            return testCaseList;
        }

        /// <summary>
        /// Gets the test case category list
        /// </summary>
        /// <param name="testCaseList">The test cases</param>
        /// <returns>Returns test cases categories</returns>
        private List<string> GetTestCaseCategortyList(List<DataType.TestCase> testCaseList)
        {
            List<string> listCaseCategory = new List<string>();
            foreach (DataType.TestCase testCase in testCaseList)
            {
                listCaseCategory.AddRange(testCase.Category);
            }
            listCaseCategory = listCaseCategory.Distinct().ToList();
            listCaseCategory.Sort();
            return listCaseCategory;
        }

        /// <summary>
        /// Gets the test case class list
        /// </summary>
        /// <param name="testCaseList">The test cases</param>
        /// <returns>Returns the test cases class list</returns>
        private List<string> GetTestCaseClassList(List<DataType.TestCase> testCaseList)
        {
            List<string> listCaseClass = new List<string>();
            foreach (DataType.TestCase testCase in testCaseList)
            {
                listCaseClass.Add(testCase.ClassType);
            }
            listCaseClass = listCaseClass.Distinct().ToList();
            listCaseClass.Sort();
            return listCaseClass;
        }

        /// <summary>
        /// Gets the path to the capture file folder from .ptfconfig file
        /// </summary>
        /// <returns>Returns a list of capture file path when the NetworkCapture is enabled. Returns null when the NetworkCapture is not enabled.</returns>
        private List<string> GetCaptureFilesPath()
        {
            List<string> captureFolders = new List<string>();
            DirectoryInfo info = new DirectoryInfo(Directory.GetCurrentDirectory());
            bool existBatch = false;
            if (info.FullName.EndsWith("Batch"))
                existBatch = true;
            string fullPath = existBatch ? info.Parent.FullName : info.FullName;
            string cfgFolder = Path.Combine(fullPath, "bin");
            try
            {
                string[] ptfconfigFiles = Directory.GetFiles(cfgFolder, "*.ptfconfig", SearchOption.TopDirectoryOnly);
                foreach (string configFile in ptfconfigFiles)
                {
                    XmlDocument configXml = new XmlDocument();
                    configXml.Load(configFile);

                    XmlNodeList groupNodes = configXml.GetElementsByTagName("Group");
                    if (groupNodes == null || groupNodes.Count <= 0) { continue; }

                    bool isNetworkCaptureEnabled = false;
                    string captureFileFolder = null;
                    foreach (XmlNode gNode in groupNodes)
                    {
                        if (gNode.Attributes["name"].Value == "NetworkCapture")
                        {
                            foreach (XmlNode pNode in gNode.ChildNodes)
                            {
                                if (pNode.Attributes["name"].Value == "Enabled")
                                {
                                    isNetworkCaptureEnabled = bool.Parse(pNode.Attributes["value"].Value);
                                }
                                if (pNode.Attributes["name"].Value == "CaptureFileFolder")
                                {
                                    captureFileFolder = pNode.Attributes["value"].Value;
                                }
                            }
                            break;
                        }
                    }

                    if (!isNetworkCaptureEnabled) { continue; }

                    if (!string.IsNullOrEmpty(captureFileFolder))
                    {
                        captureFolders.Add(captureFileFolder);
                    }
                }
            }
            catch
            {
                return captureFolders;
            }
            return captureFolders;
        }

        /// <summary>
        /// Copies a specified capture file to the log folder and returns the destination path.
        /// </summary>
        /// <param name="caseName">The specified case name</param> 
        /// <param name="captureFolder">The path of the destination capture folder</param>
        /// <returns>Returns the destination path of the specified case name</returns>
        private string CopyCaptureAndReturnPath(string caseName, string captureFolder)
        {
            List<string> srcCaptureFolders = GetCaptureFilesPath();

            foreach (string srcCapturePath in srcCaptureFolders)
            {
                if (string.IsNullOrEmpty(srcCapturePath) || !Directory.Exists(srcCapturePath))
                {
                    continue;
                }
                string[] files = Directory.GetFiles(srcCapturePath, string.Format("*{0}.etl", caseName));
                foreach (var file in files)
                {
                    string captureName = Path.GetFileNameWithoutExtension(file); // Get file name
                    captureName = captureName.Substring(captureName.IndexOf('#') + 1);
                    if (String.Compare(captureName, caseName, true) != 0)
                        continue;
                    string desCapturePath = Path.Combine(captureFolder, Path.GetFileName(file));
                    if (File.Exists(desCapturePath))
                    {
                        File.Delete(desCapturePath);
                    }
                    File.Copy(file, desCapturePath);
                    return desCapturePath;
                }
            }

            return null;
        }

        private string Serialize<T>(T obj)
        {
            return JsonSerializer.Serialize(obj);
        }
    }
}
