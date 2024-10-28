using UnityEngine;
using System.Collections.Generic;
using DoubTech.ThirdParty.AI.Common.Attributes;

namespace DoubTech.AI.Art.Runpod
{
    /// <summary>
    /// Configuration for the Runpod API, including API token, endpoint URL,
    /// input JSON template, and variables for placeholder replacement.
    /// </summary>
    [CreateAssetMenu(fileName = "RunpodConfig", menuName = "DoubTech/AI/Art/RunpodConfig", order = 1)]
    public class RunpodConfig : ScriptableObject
    {
        [Header("API Configuration")]
        [Tooltip("Your Runpod API token.")]
        [Password]
        public string token;

        [Tooltip("The base endpoint URL for the Runpod API. Do not include paths like /run or /status.")]
        public string endpointUrl;

        [Tooltip("The input JSON template for the API request. You can use variables in the format ${variableName}.")]
        public TextAsset inputJson;

        [Header("Variables")]
        [Tooltip("Variables to replace placeholders in the input JSON. Placeholders should be in the format ${variableName}.")]
        public List<Variable> variables = new List<Variable>();
    }

    /// <summary>
    /// Data types for variables.
    /// </summary>
    public enum DataType
    {
        Float,
        Int,
        Text,
        TextArea
    }

    /// <summary>
    /// Represents a key-value pair for variable replacement in the input JSON.
    /// </summary>
    [System.Serializable]
    public class Variable
    {
        [Tooltip("The name of the variable. Corresponds to the placeholder in the input JSON.")]
        public string key;

        [Tooltip("The value to replace the placeholder with.")]
        public string value;

        [Tooltip("The data type of the variable.")]
        public DataType dataType = DataType.Text;
    }
}