using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AntennaAV.ViewModels
{
    /// <summary>
    /// ObservableCollection с поддержкой массовых операций (AddRange, ReplaceRange) и минимизацией событий.
    /// </summary>
    public class FastObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification = false;

        public FastObservableCollection() : base() { }
        public FastObservableCollection(IEnumerable<T> collection) : base(collection) { }

        /// <summary>
        /// Добавляет диапазон элементов с одним событием CollectionChanged.
        /// </summary>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _suppressNotification = true;
            foreach (var item in items)
                Add(item);
            _suppressNotification = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// Заменяет все элементы коллекции на новые с одним событием CollectionChanged.
        /// </summary>
        public void ReplaceRange(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _suppressNotification = true;
            Clear();
            foreach (var item in items)
                Add(item);
            _suppressNotification = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
                base.OnCollectionChanged(e);
        }
    }
} 