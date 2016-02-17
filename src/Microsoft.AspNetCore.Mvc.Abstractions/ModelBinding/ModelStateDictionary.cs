// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Mvc.ModelBinding
{
    /// <summary>
    /// Represents the state of an attempt to bind values from an HTTP Request to an action method, which includes
    /// validation information.
    /// </summary>
    public class ModelStateDictionary : IDictionary<string, ModelStateEntry>
    {
        // Make sure to update the doc headers if this value is changed.
        /// <summary>
        /// The default value for <see cref="MaxAllowedErrors"/> of <c>200</c>.
        /// </summary>
        public static readonly int DefaultMaxAllowedErrors = 200;

        private static readonly StringSegment EmptyStringSegment = new StringSegment(buffer: string.Empty);
        private static readonly char[] Delimiters = new char[] { '.', '[' };

        private readonly ModelStateNode _root;
        private int _maxAllowedErrors;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelStateDictionary"/> class.
        /// </summary>
        public ModelStateDictionary()
            : this(DefaultMaxAllowedErrors)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelStateDictionary"/> class.
        /// </summary>
        public ModelStateDictionary(int maxAllowedErrors)
        {
            MaxAllowedErrors = maxAllowedErrors;
            _root = new ModelStateNode(parent: null, subKey: EmptyStringSegment, key: EmptyStringSegment);
        }

        public ModelStateEntry Root => _root;

        /// <summary>
        /// Gets or sets the maximum allowed model state errors in this instance of <see cref="ModelStateDictionary"/>.
        /// Defaults to <c>200</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="ModelStateDictionary"/> tracks the number of model errors added by calls to
        /// <see cref="AddModelError(string, Exception, ModelMetadata)"/> or
        /// <see cref="TryAddModelError(string, Exception, ModelMetadata)"/>.
        /// Once the value of <code>MaxAllowedErrors - 1</code> is reached, if another attempt is made to add an error,
        /// the error message will be ignored and a <see cref="TooManyModelErrorsException"/> will be added.
        /// </para>
        /// <para>
        /// Errors added via modifying <see cref="ModelStateEntry"/> directly do not count towards this limit.
        /// </para>
        /// </remarks>
        public int MaxAllowedErrors
        {
            get
            {
                return _maxAllowedErrors;
            }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _maxAllowedErrors = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether or not the maximum number of errors have been
        /// recorded.
        /// </summary>
        /// <remarks>
        /// Returns <c>true</c> if a <see cref="TooManyModelErrorsException"/> has been recorded;
        /// otherwise <c>false</c>.
        /// </remarks>
        public bool HasReachedMaxErrors
        {
            get { return ErrorCount >= MaxAllowedErrors; }
        }

        /// <summary>
        /// Gets the number of errors added to this instance of <see cref="ModelStateDictionary"/> via
        /// <see cref="AddModelError"/> or <see cref="TryAddModelError"/>.
        /// </summary>
        public int ErrorCount { get; private set; }

        /// <inheritdoc />
        public int Count
        {
            get
            {
                var count = 0;
                foreach (var item in this)
                {
                    count++;
                }

                return count;
            }
        }

        /// <inheritdoc />
        public bool IsReadOnly
        {
            get { return false; }
        }

        /// <summary>
        /// Gets the key collection.
        /// </summary>
        public KeyCollection Keys => new KeyCollection(this);

        /// <inheritdoc />
        ICollection<string> IDictionary<string, ModelStateEntry>.Keys => Keys;

        /// <summary>
        /// Gets the value collection.
        /// </summary>
        public ValueCollection Values => new ValueCollection(this);

        /// <inheritdoc />
        ICollection<ModelStateEntry> IDictionary<string, ModelStateEntry>.Values => Values;

        /// <summary>
        /// Gets a value that indicates whether any model state values in this model state dictionary is invalid or not validated.
        /// </summary>
        public bool IsValid
        {
            get
            {
                return ValidationState == ModelValidationState.Valid || ValidationState == ModelValidationState.Skipped;
            }
        }

        /// <inheritdoc />
        public ModelValidationState ValidationState
        {
            get
            {
                var entries = FindKeysWithPrefix(string.Empty);
                return GetValidity(entries, defaultState: ModelValidationState.Valid);
            }
        }

        /// <inheritdoc />
        public ModelStateEntry this[string key]
        {
            get
            {
                if (key == null)
                {
                    throw new ArgumentNullException(nameof(key));
                }

                return GetModelStateForKey(key);
            }
            set
            {
                if (key == null)
                {
                    throw new ArgumentNullException(nameof(key));
                }

                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                var entry = GetModelStateForKey(key);
                entry.Copy(value);
                entry.Visible = true;
            }
        }

        // Flag that indiciates if TooManyModelErrorException has already been added to this dictionary.
        private bool HasRecordedMaxModelError { get; set; }

        /// <summary>
        /// Adds the specified <paramref name="exception"/> to the <see cref="ModelStateEntry.Errors"/> instance
        /// that is associated with the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelStateEntry"/> to add errors to.</param>
        /// <param name="exception">The <see cref="Exception"/> to add.</param>
        public void AddModelError(string key, Exception exception, ModelMetadata metadata)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            TryAddModelError(key, exception, metadata);
        }

        /// <summary>
        /// Attempts to add the specified <paramref name="exception"/> to the <see cref="ModelStateEntry.Errors"/>
        /// instance that is associated with the specified <paramref name="key"/>. If the maximum number of allowed
        /// errors has already been recorded, records a <see cref="TooManyModelErrorsException"/> exception instead.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelStateEntry"/> to add errors to.</param>
        /// <param name="exception">The <see cref="Exception"/> to add.</param>
        /// <returns>
        /// <c>True</c> if the given error was added, <c>false</c> if the error was ignored.
        /// See <see cref="MaxAllowedErrors"/>.
        /// </returns>
        public bool TryAddModelError(string key, Exception exception, ModelMetadata metadata)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (ErrorCount >= MaxAllowedErrors - 1)
            {
                EnsureMaxErrorsReachedRecorded();
                return false;
            }

            if (exception is FormatException || exception is OverflowException)
            {
                // Convert FormatExceptions and OverflowExceptions to Invalid value messages.
                ModelStateEntry entry;
                TryGetValue(key, out entry);

                var name = metadata.GetDisplayName();
                string errorMessage;
                if (entry == null)
                {
                    errorMessage = metadata.ModelBindingMessageProvider.UnknownValueIsInvalidAccessor(name);
                }
                else
                {
                    errorMessage = metadata.ModelBindingMessageProvider.AttemptedValueIsInvalidAccessor(
                        entry.AttemptedValue,
                        name);
                }

                return TryAddModelError(key, errorMessage);
            }

            ErrorCount++;
            AddModelErrorCore(key, exception);
            return true;
        }

        /// <summary>
        /// Adds the specified <paramref name="errorMessage"/> to the <see cref="ModelStateEntry.Errors"/> instance
        /// that is associated with the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelStateEntry"/> to add errors to.</param>
        /// <param name="errorMessage">The error message to add.</param>
        public void AddModelError(string key, string errorMessage)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (errorMessage == null)
            {
                throw new ArgumentNullException(nameof(errorMessage));
            }

            TryAddModelError(key, errorMessage);
        }

        /// <summary>
        /// Attempts to add the specified <paramref name="errorMessage"/> to the <see cref="ModelStateEntry.Errors"/>
        /// instance that is associated with the specified <paramref name="key"/>. If the maximum number of allowed
        /// errors has already been recorded, records a <see cref="TooManyModelErrorsException"/> exception instead.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelStateEntry"/> to add errors to.</param>
        /// <param name="errorMessage">The error message to add.</param>
        /// <returns>
        /// <c>True</c> if the given error was added, <c>false</c> if the error was ignored.
        /// See <see cref="MaxAllowedErrors"/>.
        /// </returns>
        public bool TryAddModelError(string key, string errorMessage)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (errorMessage == null)
            {
                throw new ArgumentNullException(nameof(errorMessage));
            }

            if (ErrorCount >= MaxAllowedErrors - 1)
            {
                EnsureMaxErrorsReachedRecorded();
                return false;
            }

            ErrorCount++;
            var modelState = GetModelStateForKey(key);
            modelState.ValidationState = ModelValidationState.Invalid;
            modelState.Visible = true;
            modelState.Errors.Add(errorMessage);

            return true;
        }

        /// <summary>
        /// Returns the aggregate <see cref="ModelValidationState"/> for items starting with the
        /// specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to look up model state errors for.</param>
        /// <returns>Returns <see cref="ModelValidationState.Unvalidated"/> if no entries are found for the specified
        /// key, <see cref="ModelValidationState.Invalid"/> if at least one instance is found with one or more model
        /// state errors; <see cref="ModelValidationState.Valid"/> otherwise.</returns>
        public ModelValidationState GetFieldValidationState(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var entries = FindKeysWithPrefix(key);
            return GetValidity(entries, defaultState: ModelValidationState.Unvalidated);
        }

        /// <summary>
        /// Returns <see cref="ModelValidationState"/> for the <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to look up model state errors for.</param>
        /// <returns>Returns <see cref="ModelValidationState.Unvalidated"/> if no entry is found for the specified
        /// key, <see cref="ModelValidationState.Invalid"/> if an instance is found with one or more model
        /// state errors; <see cref="ModelValidationState.Valid"/> otherwise.</returns>
        public ModelValidationState GetValidationState(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            ModelStateEntry validationState;
            if (TryGetValue(key, out validationState))
            {
                return validationState.ValidationState;
            }

            return ModelValidationState.Unvalidated;
        }

        /// <summary>
        /// Marks the <see cref="ModelStateEntry.ValidationState"/> for the entry with the specified
        /// <paramref name="key"/> as <see cref="ModelValidationState.Valid"/>.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelStateEntry"/> to mark as valid.</param>
        public void MarkFieldValid(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var modelState = GetModelStateForKey(key);
            if (modelState.ValidationState == ModelValidationState.Invalid)
            {
                throw new InvalidOperationException(Resources.Validation_InvalidFieldCannotBeReset);
            }

            modelState.Visible = true;
            modelState.ValidationState = ModelValidationState.Valid;
        }

        /// <summary>
        /// Marks the <see cref="ModelStateEntry.ValidationState"/> for the entry with the specified <paramref name="key"/>
        /// as <see cref="ModelValidationState.Skipped"/>.
        /// </summary>
        /// <param name="key">The key of the <see cref="ModelStateEntry"/> to mark as skipped.</param>
        public void MarkFieldSkipped(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var modelState = GetModelStateForKey(key);
            if (modelState.ValidationState == ModelValidationState.Invalid)
            {
                throw new InvalidOperationException(Resources.Validation_InvalidFieldCannotBeReset_ToSkipped);
            }

            modelState.Visible = true;
            modelState.ValidationState = ModelValidationState.Skipped;
        }

        /// <summary>
        /// Copies the values from the specified <paramref name="dictionary"/> into this instance, overwriting
        /// existing values if keys are the same.
        /// </summary>
        /// <param name="dictionary">The <see cref="ModelStateDictionary"/> to copy values from.</param>
        public void Merge(ModelStateDictionary dictionary)
        {
            if (dictionary == null)
            {
                return;
            }

            foreach (var entry in dictionary)
            {
                this[entry.Key.Value] = entry.Value;
            }
        }

        /// <summary>
        /// Sets the of <see cref="ModelStateEntry.RawValue"/> and <see cref="ModelStateEntry.AttemptedValue"/> for
        /// the <see cref="ModelStateEntry"/> with the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key for the <see cref="ModelStateEntry"/> entry.</param>
        /// <param name="rawvalue">The raw value for the <see cref="ModelStateEntry"/> entry.</param>
        /// <param name="attemptedValue">
        /// The values of <param name="rawValue"/> in a comma-separated <see cref="string"/>.
        /// </param>
        public void SetModelValue(string key, object rawValue, string attemptedValue)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var modelState = GetModelStateForKey(key);
            modelState.RawValue = rawValue;
            modelState.AttemptedValue = attemptedValue;
            modelState.Visible = true;
        }

        /// <summary>
        /// Sets the value for the <see cref="ModelStateEntry"/> with the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key for the <see cref="ModelStateEntry"/> entry</param>
        /// <param name="valueProviderResult">
        /// A <see cref="ValueProviderResult"/> with data for the <see cref="ModelStateEntry"/> entry.
        /// </param>
        public void SetModelValue(string key, ValueProviderResult valueProviderResult)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            // Avoid creating a new array for rawValue if there's only one value.
            object rawValue;
            if (valueProviderResult == ValueProviderResult.None)
            {
                rawValue = null;
            }
            else if (valueProviderResult.Length == 1)
            {
                rawValue = valueProviderResult.Values[0];
            }
            else
            {
                rawValue = valueProviderResult.Values.ToArray();
            }

            SetModelValue(key, rawValue, valueProviderResult.ToString());
        }

        /// <summary>
        /// Clears <see cref="ModelStateDictionary"/> entries that match the key that is passed as parameter.
        /// </summary>
        /// <param name="key">The key of <see cref="ModelStateDictionary"/> to clear.</param>
        public void ClearValidationState(string key)
        {
            // If key is null or empty, clear all entries in the dictionary
            // else just clear the ones that have key as prefix
            var entries = FindKeysWithPrefix(key ?? string.Empty);
            foreach (var entry in entries)
            {
                entry.Value.Errors.Clear();
                entry.Value.ValidationState = ModelValidationState.Unvalidated;
            }
        }

        private ModelStateNode GetModelStateForKey(string key)
        {
            Debug.Assert(key != null);
            if (key.Length == 0)
            {
                return _root;
            }

            var currentRoot = _root;
            var previousIndex = 0;
            int index;
            while ((index = key.IndexOfAny(Delimiters, previousIndex)) != -1)
            {
                index = GetArrayIndexDelimiter(key, index);
                if (index == -1)
                {
                    break;
                }

                var subKey = new StringSegment(key, previousIndex, index - previousIndex);
                var fullKey = new StringSegment(key, 0, index);
                currentRoot = GetOrAddChildNode(currentRoot, subKey, fullKey);
                previousIndex = index + 1;
            }

            if (previousIndex < key.Length - 1)
            {
                var subKey = new StringSegment(key, previousIndex, key.Length - previousIndex);
                var fullKey = new StringSegment(key, 0, key.Length);
                currentRoot = GetOrAddChildNode(currentRoot, subKey, fullKey);
            }

            return currentRoot;
        }

        private static int GetArrayIndexDelimiter(string key, int index)
        {
            if (key[index] == '[')
            {
                var closeParanthesesIndex = key.IndexOf(']', index + 1);
                if (closeParanthesesIndex == -1)
                {
                    // Key: "person.Addresses[0.BadKey" Consume the key in entirety.
                    return -1;
                }
                else if (closeParanthesesIndex < key.Length - 1)
                {
                    if (key[closeParanthesesIndex + 1] == '.')
                    {
                        // person.Addresses[0].Street
                        // Create a prefix node for 'Addresses[0].'
                        index = closeParanthesesIndex + 1;
                    }
                    else
                    {
                        // person.Addresses[0]Street. Consume the key in entirety
                        return -1;
                    }
                }
                else
                {
                    // person.CarIds[17]. Consume the key in entirety.
                    return -1;
                }
            }

            return index;
        }

        private ModelStateNode FindNode(string key)
        {
            if (key.Length == 0)
            {
                return _root;
            }

            var currentRoot = _root;
            var previousIndex = 0;
            int index;
            while ((index = key.IndexOfAny(Delimiters, previousIndex)) != -1)
            {
                index = GetArrayIndexDelimiter(key, index);
                if (index == -1)
                {
                    break;
                }

                var subKey = new StringSegment(key, previousIndex, index - previousIndex);
                var next = GetChildNode(currentRoot, subKey);
                if (next == null)
                {
                    return null;
                }

                currentRoot = next;
                previousIndex = index + 1;
            }

            if (previousIndex < key.Length - 1)
            {
                var subKey = new StringSegment(key, previousIndex, key.Length - previousIndex);
                currentRoot = GetChildNode(currentRoot, subKey);
            }

            return currentRoot;
        }

        private static ModelStateNode GetChildNode(ModelStateNode currentRoot, StringSegment subKey)
        {
            for (var i = 0; currentRoot.Children != null && i < currentRoot.Children.Count; i++)
            {
                var child = currentRoot.Children[i];
                if (child.SubKey.Equals(subKey, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        private static ModelStateNode GetOrAddChildNode(
            ModelStateNode root, 
            StringSegment subKey,
            StringSegment key)
        {
            ModelStateNode child;
            if (root.Children == null)
            {
                child = new ModelStateNode(root, subKey, key);
                root.Children = new List<ModelStateNode>(1);
            }
            else
            {
                for (var i = 0; i < root.Children.Count; i++)
                {
                    child = root.Children[i];
                    if (child.SubKey.Equals(subKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return child;
                    }
                }

                child = new ModelStateNode(root, subKey, key);
            }

            root.Children.Add(child);
            return child;
        }

        private static ModelValidationState GetValidity(PrefixEnumerable entries, ModelValidationState defaultState)
        {
            var hasEntries = false;
            var validationState = ModelValidationState.Valid;

            foreach (var entry in entries)
            {
                hasEntries = true;

                var entryState = entry.Value.ValidationState;
                if (entryState == ModelValidationState.Unvalidated)
                {
                    // If any entries of a field is unvalidated, we'll treat the tree as unvalidated.
                    return entryState;
                }
                else if (entryState == ModelValidationState.Invalid)
                {
                    validationState = entryState;
                }
            }

            return hasEntries ? validationState : defaultState;
        }

        private void EnsureMaxErrorsReachedRecorded()
        {
            if (!HasRecordedMaxModelError)
            {
                var exception = new TooManyModelErrorsException(Resources.ModelStateDictionary_MaxModelStateErrors);
                AddModelErrorCore(string.Empty, exception);
                HasRecordedMaxModelError = true;
                ErrorCount++;
            }
        }

        private void AddModelErrorCore(string key, Exception exception)
        {
            var modelState = GetModelStateForKey(key);
            modelState.ValidationState = ModelValidationState.Invalid;
            modelState.Visible = true;
            modelState.Errors.Add(exception);
        }

        /// <inheritdoc />
        public void Add(KeyValuePair<string, ModelStateEntry> item)
        {
            Add(item.Key, item.Value);
        }

        /// <inheritdoc />
        public void Add(string key, ModelStateEntry value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (ContainsKey(key))
            {
                throw new ArgumentException(Resources.ModelStateDictionary_DuplicateKey, nameof(key));
            }

            this[key] = value;
        }

        /// <inheritdoc />
        public void Clear()
        {
            _root.Reset();
            _root.Children.Clear();
        }

        /// <inheritdoc />
        public bool Contains(KeyValuePair<string, ModelStateEntry> item)
        {
            if (item.Key == null)
            {
                return false;
            }

            var node = FindNode(item.Key);
            return node?.Equals(item.Value) ?? false;
        }

        /// <inheritdoc />
        public bool ContainsKey(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return FindNode(key)?.Visible ?? false;
        }

        /// <inheritdoc />
        public void CopyTo(KeyValuePair<string, ModelStateEntry>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0 || arrayIndex + Count >= array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }

            foreach (var item in this)
            {
                array[arrayIndex++] = new KeyValuePair<string, ModelStateEntry>(item.Key.Value, item.Value);
            }
        }

        /// <inheritdoc />
        public bool Remove(KeyValuePair<string, ModelStateEntry> item)
        {
            if (item.Key == null)
            {
                return false;
            }

            return Contains(item) && Remove(item.Key);
        }

        /// <inheritdoc />
        public bool Remove(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var node = FindNode(key);
            if (node?.Visible == true)
            {
                node.Reset();
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public bool TryGetValue(string key, out ModelStateEntry value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var result = FindNode(key);
            if (result?.Visible == true)
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<StringSegment, ModelStateEntry>> GetEnumerator()
            => new PrefixEnumerator(this, prefix: string.Empty);

        /// <inheritdoc />
        IEnumerator<KeyValuePair<string, ModelStateEntry>> IEnumerable<KeyValuePair<string, ModelStateEntry>>.GetEnumerator()
            => new StringPrefixEnumerator(this, prefix: string.Empty);

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => new StringPrefixEnumerator(this, prefix: string.Empty);

        public static bool StartsWithPrefix(string prefix, string key)
        {
            if (prefix == null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (prefix.Length == 0)
            {
                // Everything is prefixed by the empty string
                return true;
            }

            if (key.Length < prefix.Length)
            {
                return false;
            }

            var subKeyIndex = 0;
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (key[0] == '[')
                {
                    subKeyIndex = key.IndexOf('.') + 1;

                    if (string.Compare(key, subKeyIndex, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        return false;
                    }
                    else if (prefix.Length == (key.Length - subKeyIndex))
                    {
                        // prefix == subKey
                        return true;
                    }
                }
                else
                {
                    return false;
                }
            }
            else if (key.Length == prefix.Length)
            {
                // key == prefix
                return true;
            }

            var charAfterPrefix = key[subKeyIndex + prefix.Length];
            switch (charAfterPrefix)
            {
                case '[':
                case '.':
                    return true;
            }

            return false;
        }

        public PrefixEnumerable FindKeysWithPrefix(string prefix)
        {
            if (prefix == null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            return new PrefixEnumerable(this, prefix);
        }

        private class ModelStateNode : ModelStateEntry
        {
            public ModelStateNode(ModelStateNode parent, StringSegment subKey, StringSegment key)
            {
                Debug.Assert(subKey != null);
                Debug.Assert(key != null);
                Parent = parent;
                SubKey = subKey;
                Key = key;
            }

            public ModelStateNode Parent { get; }

            public List<ModelStateNode> Children { get; set; }

            public bool Visible { get; set; }

            public StringSegment SubKey { get; }

            public StringSegment Key { get; }

            public void Copy(ModelStateEntry entry)
            {
                RawValue = entry.RawValue;
                AttemptedValue = entry.AttemptedValue;
                Errors.Clear();
                for (var i = 0; i < entry.Errors.Count; i++)
                {
                    Errors.Add(entry.Errors[i]);
                }

                ValidationState = entry.ValidationState;
                Visible = true;
            }

            public void Reset()
            {
                Visible = false;
                RawValue = null;
                AttemptedValue = null;
                Errors.Clear();
            }
        }

        public struct PrefixEnumerable : IEnumerable<KeyValuePair<StringSegment, ModelStateEntry>>
        {
            private readonly ModelStateDictionary _dictionary;
            private readonly string _prefix;

            public PrefixEnumerable(ModelStateDictionary dictionary, string prefix)
            {
                if (dictionary == null)
                {
                    throw new ArgumentNullException(nameof(dictionary));
                }

                if (prefix == null)
                {
                    throw new ArgumentNullException(nameof(prefix));
                }

                _dictionary = dictionary;
                _prefix = prefix;
            }

            public PrefixEnumerator GetEnumerator() => new PrefixEnumerator(_dictionary, _prefix);

            IEnumerator<KeyValuePair<StringSegment, ModelStateEntry>>
                IEnumerable<KeyValuePair<StringSegment, ModelStateEntry>>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct PrefixEnumerator : IEnumerator<KeyValuePair<StringSegment, ModelStateEntry>>
        {
            private readonly List<EnumeratorEntry> _nodes;
            private readonly ModelStateNode _rootNode;
            private ModelStateNode _modelStateNode;
            private int _index;

            public PrefixEnumerator(ModelStateDictionary dictionary, string prefix)
            {
                if (dictionary == null)
                {
                    throw new ArgumentNullException(nameof(dictionary));
                }

                if (prefix == null)
                {
                    throw new ArgumentNullException(nameof(prefix));
                }

                _index = 0;
                _rootNode = dictionary.FindNode(prefix);
                _nodes = new List<EnumeratorEntry>
                {
                    new EnumeratorEntry(_rootNode)
                };
                _modelStateNode = null;
            }

            public KeyValuePair<StringSegment, ModelStateEntry> Current =>
                new KeyValuePair<StringSegment, ModelStateEntry>(_modelStateNode.Key, _modelStateNode);

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (_rootNode == null)
                {
                    return false;
                }

                while (_nodes.Count > 0)
                {
                    var entry = _nodes[0];
                    if (entry.ModelStateNode != null)
                    {
                        var node = entry.ModelStateNode;
                        _nodes.RemoveAt(0);
                        if (node.Children != null)
                        {
                            _nodes.Add(new EnumeratorEntry(node.Children));
                        }

                        if (node.Visible)
                        {
                            _modelStateNode = node;
                            return true;
                        }

                        continue;
                    }

                    while (_index < entry.ModelStateNodes.Count)
                    {
                        var node = entry.ModelStateNodes[_index];
                        if (_index == entry.ModelStateNodes.Count - 1)
                        {
                            // We've exhausted the current sublist.
                            _nodes.RemoveAt(0);
                            _index = 0;
                        }
                        else
                        {
                            _index++;
                        }

                        if (node.Children != null)
                        {
                            _nodes.Add(new EnumeratorEntry(node.Children));
                        }

                        if (node.Visible)
                        {
                            _modelStateNode = node;
                            return true;
                        }

                        if (_index == 0)
                        {
                            break;
                        }
                    }
                }

                return false;
            }

            public void Reset()
            {
                _index = 0;
                _nodes.Clear();
                _nodes.Add(new EnumeratorEntry(_rootNode));
                _modelStateNode = null;
            }
        }

        private struct StringPrefixEnumerator : IEnumerator<KeyValuePair<string, ModelStateEntry>>
        {
            private PrefixEnumerator _prefixEnumerator;

            public StringPrefixEnumerator(ModelStateDictionary dictionary, string prefix)
            {
                _prefixEnumerator = new PrefixEnumerator(dictionary, prefix);
                Current = default(KeyValuePair<string, ModelStateEntry>);
            }

            public KeyValuePair<string, ModelStateEntry> Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose() => _prefixEnumerator.Dispose();

            public bool MoveNext()
            {
                var result = _prefixEnumerator.MoveNext();
                if (result)
                {
                    var current = _prefixEnumerator.Current;
                    Current = new KeyValuePair<string, ModelStateEntry>(current.Key.Value, current.Value);
                }
                else
                {
                    Current = default(KeyValuePair<string, ModelStateEntry>);
                }

                return result;
            }

            public void Reset()
            {
                _prefixEnumerator.Reset();
                Current = default(KeyValuePair<string, ModelStateEntry>);
            }
        }

        public struct KeyCollection : ICollection<string>
        {
            private readonly ModelStateDictionary _dictionary;

            public KeyCollection(ModelStateDictionary dictionary)
            {
                _dictionary = dictionary;
            }

            public int Count => _dictionary.Count;

            public bool IsReadOnly => true;

            public void Add(string item)
            {
                throw new NotSupportedException();
            }

            public void Clear()
            {
                throw new NotSupportedException();
            }

            public bool Contains(string item)
            {
                if (item == null)
                {
                    return false;
                }

                return _dictionary.ContainsKey(item);
            }

            public void CopyTo(string[] array, int arrayIndex)
            {
                if (array == null)
                {
                    throw new ArgumentNullException(nameof(array));
                }

                if (arrayIndex < 0 || arrayIndex + Count > array.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex));
                }

                foreach (var item in this)
                {
                    array[arrayIndex++] = item;
                }
            }

            public bool Remove(string item)
            {
                throw new NotSupportedException();
            }

            public KeyEnumerator GetEnumerator() => new KeyEnumerator(_dictionary, prefix: string.Empty);

            IEnumerator<string> IEnumerable<string>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct KeyEnumerator : IEnumerator<string>
        {
            private PrefixEnumerator _prefixEnumerator;

            public KeyEnumerator(ModelStateDictionary dictionary, string prefix)
            {
                _prefixEnumerator = new PrefixEnumerator(dictionary, prefix);
                Current = null;
            }

            public string Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose() => _prefixEnumerator.Dispose();

            public bool MoveNext()
            {
                var result = _prefixEnumerator.MoveNext();
                if (result)
                {
                    var current = _prefixEnumerator.Current;
                    Current = current.Key.Value;
                }
                else
                {
                    Current = null;
                }

                return result;
            }

            public void Reset()
            {
                _prefixEnumerator.Reset();
                Current = null;
            }
        }

        public struct ValueCollection : ICollection<ModelStateEntry>
        {
            private readonly ModelStateDictionary _dictionary;

            public ValueCollection(ModelStateDictionary dictionary)
            {
                _dictionary = dictionary;
            }

            public int Count => _dictionary.Count;

            public bool IsReadOnly => true;

            public void Add(ModelStateEntry item)
            {
                throw new NotSupportedException();
            }

            public void Clear()
            {
                throw new NotSupportedException();
            }

            public bool Contains(ModelStateEntry item)
            {
                if (item == null)
                {
                    return false;
                }

                foreach (var existingItem in this)
                {
                    if (existingItem.Equals(item))
                    {
                        return true;
                    }
                }

                return false;
            }

            public void CopyTo(ModelStateEntry[] array, int arrayIndex)
            {
                if (array == null)
                {
                    throw new ArgumentNullException(nameof(array));
                }

                if (arrayIndex < 0 || arrayIndex + Count > array.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex));
                }

                foreach (var item in this)
                {
                    array[arrayIndex++] = item;
                }
            }

            public bool Remove(ModelStateEntry item)
            {
                throw new NotSupportedException();
            }

            public ValueEnumerator GetEnumerator() => new ValueEnumerator(_dictionary, prefix: string.Empty);

            IEnumerator<ModelStateEntry> IEnumerable<ModelStateEntry>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct ValueEnumerator : IEnumerator<ModelStateEntry>
        {
            private PrefixEnumerator _prefixEnumerator;

            public ValueEnumerator(ModelStateDictionary dictionary, string prefix)
            {
                _prefixEnumerator = new PrefixEnumerator(dictionary, prefix);
                Current = null;
            }

            public ModelStateEntry Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose() => _prefixEnumerator.Dispose();

            public bool MoveNext()
            {
                var result = _prefixEnumerator.MoveNext();
                if (result)
                {
                    var current = _prefixEnumerator.Current;
                    Current = current.Value;
                }
                else
                {
                    Current = null;
                }

                return result;
            }

            public void Reset()
            {
                _prefixEnumerator.Reset();
                Current = null;
            }
        }

        private struct EnumeratorEntry
        {
            public EnumeratorEntry(ModelStateNode modelStateNode)
            {
                ModelStateNode = modelStateNode;
                ModelStateNodes = null;
            }

            public EnumeratorEntry(List<ModelStateNode> modelStateNodes)
            {
                ModelStateNode = null;
                ModelStateNodes = modelStateNodes;
            }

            public ModelStateNode ModelStateNode { get; }

            public List<ModelStateNode> ModelStateNodes { get; }
        }
    }
}
