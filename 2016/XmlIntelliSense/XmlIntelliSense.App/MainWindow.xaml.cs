﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.XPath;
using ICSharpCode.AvalonEdit.CodeCompletion;
using XmlIntelliSense.App.SharpDevelopXmlEditor;
using XmlIntelliSense.App.XHelper;
using XmlIntelliSense.App.XSemmel;

namespace XmlIntelliSense.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            textEditor.Text = File.ReadAllText("Sample.xml");
            XSD.Text = File.ReadAllText("Schema.xsd");

            XmlSchemaSet schemas = new XmlSchemaSet();
            schemas.Add("", XmlReader.Create(new StringReader(File.ReadAllText("Schema.xsd"))));
            schemas.Compile();
            XsdInformation = XHelper.XsdParser.AnalyseSchema(schemas);


            textEditor.TextChanged += IntelliSense;
            textEditor.TextArea.TextEntered += TextArea_TextEntered;
            textEditor.TextArea.Caret.PositionChanged += IntelliSense;
        }

        public List<XsdInformationElement> XsdInformation { get; set; }

        private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            var _editor = textEditor;

            try
            {

                switch (e.Text)
                {
                    case ">":
                        {
                            //auto-insert closing element
                            int offset = _editor.CaretOffset;
                            string s = XHelper.XmlParser.GetElementAtCursor(_editor.Text, offset - 1);
                            if (!string.IsNullOrWhiteSpace(s) && "!--" != s)
                            {
                                if (!XHelper.XmlParser.IsClosingElement(_editor.Text, offset - 1, s))
                                {
                                    string endElement = "</" + s + ">";
                                    var rightOfCursor = _editor.Text.Substring(offset, Math.Max(0, Math.Min(endElement.Length + 50, _editor.Text.Length) - offset - 1)).TrimStart();
                                    if (!rightOfCursor.StartsWith(endElement))
                                    {
                                        _editor.TextArea.Document.Insert(offset, endElement);
                                        _editor.CaretOffset = offset;
                                    }
                                }
                            }
                            break;
                        }
                    case "/":
                        {
                            //insert name of closing element
                            int offset = _editor.CaretOffset;
                            if (offset > 1 && _editor.Text[offset - 2] == '<')
                            {
                                //expand to closing tag
                                string s = XParser.GetParentElementAtCursor(_editor.Text, offset - 1);
                                if (!string.IsNullOrEmpty(s))
                                {
                                    //   showCompletion(new List<ICompletionData>
                                    //   {
                                    //       new MyCompletionData(s + ">")
                                    //});
                                    //  return true;
                                }
                            }
                            if (_editor.Text.Length > offset + 2 && _editor.Text[offset] == '>')
                            {
                                //remove closing tag if exist
                                string s = XHelper.XmlParser.GetElementAtCursor(_editor.Text, offset - 1);
                                if (!string.IsNullOrWhiteSpace(s))
                                {
                                    //search closing end tag. Element must be empty (whitespace allowed)  
                                    //"<hallo>  </hallo>" --> enter '/' --> "<hallo/>  "
                                    string expectedEndTag = "</" + s + ">";
                                    for (int i = offset + 1; i < _editor.Text.Length - expectedEndTag.Length + 1; i++)
                                    {
                                        if (!char.IsWhiteSpace(_editor.Text[i]))
                                        {
                                            if (_editor.Text.Substring(i, expectedEndTag.Length) == expectedEndTag)
                                            {
                                                //remove already existing endTag
                                                _editor.Document.Remove(i, expectedEndTag.Length);
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                            break;
                        }
                    case "<":
                        var parentElement = XHelper.XmlParser.GetParentElementPath(_editor.Text);
                        var elementAutocompleteList = ProvidePossibleElementsAutocomplete(parentElement);

                        InvokeCompletionWindow(elementAutocompleteList, false);

                        break;
                    case " ":
                        {
                            var currentElement = SharpDevelopXmlEditor.XmlParser.GetActiveElementStartPath(_editor.Text, _editor.CaretOffset);
                            var attributeautocompletelist = ProvidePossibleAttributesAutocomplete(currentElement);
                            InvokeCompletionWindow(attributeautocompletelist, true);
                            break;
                        }

                }
            }
            catch (Exception exc)
            {

            }

            if (e.Text.Length > 0)
            {
                char c = e.Text[0];
                if (!(char.IsLetterOrDigit(c) || char.IsPunctuation(c)))
                {
                    // Whenever a non-letter is typed while the completion window is open,
                    // insert the currently selected element.
                    //_completionWindow.CompletionList.RequestInsertion(e);
                    e.Handled = true;
                }
            }
        }

        private void InvokeCompletionWindow(List<Tuple<string, string>> elementAutocompleteList, bool isAttribute)
        {
            var completionWindow = new CompletionWindow(textEditor.TextArea);
            IList<ICompletionData> data = completionWindow.CompletionList.CompletionData;
            if (elementAutocompleteList.Any())
            {
                foreach (var autocompleteelement in elementAutocompleteList)
                {
                    data.Add(new XmlCompletionData(autocompleteelement.Item1, autocompleteelement.Item2, isAttribute));
                }
                completionWindow.Show();
                completionWindow.Closed += delegate { completionWindow = null; };
            }
        }

        public List<Tuple<string, string>> ProvidePossibleElementsAutocomplete(XmlElementPath path)
        {
            List<Tuple<string, string>> result = new List<Tuple<string, string>>();

            if (path.IsEmpty)
            {
                var xsdResultForGivenElementPath = XsdInformation.FirstOrDefault(x => x.IsRoot);

                if (xsdResultForGivenElementPath != null)
                {
                    result.Add(new Tuple<string, string>(xsdResultForGivenElementPath.Name, xsdResultForGivenElementPath.DataType));
                }
            }
            else
            {
                StringBuilder xpath = new StringBuilder();
                xpath.Append("/");
                foreach (var element in path.Elements)
                {
                    xpath.Append("/" + element.Name);
                }

                var xsdResultForGivenElementPath = XsdInformation.FirstOrDefault(x => x.XPathLikeKey.ToLowerInvariant() == xpath.ToString().ToLowerInvariant());

                if (xsdResultForGivenElementPath != null)
                {
                    foreach (var xsdInformationElement in xsdResultForGivenElementPath.Elements)
                    {
                        result.Add(new Tuple<string, string>(xsdInformationElement.Name, xsdInformationElement.DataType));
                    }
                }
            }
          

            return result;
        }

        public List<Tuple<string, string>> ProvidePossibleAttributesAutocomplete(XmlElementPath path)
        {
            List<Tuple<string, string>> result = new List<Tuple<string, string>>();

            if (path.IsEmpty)
            {
                var xsdResultForGivenElementPath = XsdInformation.FirstOrDefault(x => x.IsRoot);

                if (xsdResultForGivenElementPath != null)
                {
                    foreach (var xsdInformationAttribute in xsdResultForGivenElementPath.Attributes)
                    {
                        result.Add(new Tuple<string, string>(xsdInformationAttribute.Name, xsdInformationAttribute.DataType));
                    }
                }
            }
            else
            {
                StringBuilder xpath = new StringBuilder();
                xpath.Append("/");
                foreach (var element in path.Elements)
                {
                    xpath.Append("/" + element.Name);
                }

                var xsdResultForGivenElementPath = XsdInformation.FirstOrDefault(x => x.XPathLikeKey.ToLowerInvariant() == xpath.ToString().ToLowerInvariant());

                if (xsdResultForGivenElementPath != null)
                {
                    foreach (var xsdInformationAttribute in xsdResultForGivenElementPath.Attributes)
                    {
                        result.Add(new Tuple<string, string>(xsdInformationAttribute.Name, xsdInformationAttribute.DataType));
                    }
                }
            }


            return result;
        }

        private void IntelliSense(object sender, EventArgs eventArgs)
        {
            //var GetActiveElementStartPath = XmlParser.GetActiveElementStartPath(textEditor.Text, textEditor.TextArea.Caret.Offset);
            //var GetActiveElementStartPathAtIndex = XmlParser.GetActiveElementStartPathAtIndex(textEditor.Text, textEditor.TextArea.Caret.Offset);
            //var GetAttributeName = XmlParser.GetAttributeName(textEditor.Text, textEditor.TextArea.Caret.Offset);
            //var GetAttributeNameAtIndex = XmlParser.GetAttributeNameAtIndex(textEditor.Text, textEditor.TextArea.Caret.Offset);
            //var GetParentElementPath = XmlParser.GetParentElementPath(textEditor.Text);

            //StringBuilder builder = new StringBuilder();
            //builder.AppendLine("GetActiveElementStartPath: " + GetActiveElementStartPath.ToString());
            //builder.AppendLine("GetActiveElementStartPathAtIndex: " + GetActiveElementStartPathAtIndex.ToString());
            //builder.AppendLine("GetAttributeName: " + GetAttributeName.ToString());
            //builder.AppendLine("GetAttributeNameAtIndex: " + GetAttributeNameAtIndex.ToString());
            //builder.AppendLine("GetParentElementPath: " + GetParentElementPath.ToString());

            //StringBuilder builder = new StringBuilder();
            //string s = XParser.GetParentElementAtCursor(textEditor.Text, textEditor.CaretOffset - 1);
            //string x = XParser.GetElementAtCursor(textEditor.Text, textEditor.CaretOffset - 1);
            //builder.AppendLine(s);
            //builder.AppendLine(x);
            //this.CurrentPath.Text = builder.ToString();
        }
    }
}
