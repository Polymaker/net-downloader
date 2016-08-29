using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Threading;
using System.Diagnostics;
using System.Security.Permissions;


namespace NetDownloader.Collections
{
    [DebuggerDisplay("Count = {Count}")]
    [HostProtection(SecurityAction.LinkDemand, Synchronization = true, ExternalThreading = true)]
    public class ThreadSafeList<T> : IEnumerable<T>, IDisposable
    {

        private readonly List<T> _Items;
        private readonly ReaderWriterLockSlim _Lock;

        public T this[int index]
        {
            get
            {
                _Lock.EnterReadLock();
                try
                {
                    return _Items[index];
                }
                finally
                {
                    _Lock.ExitReadLock();
                }
            }
            set
            {
                _Lock.EnterWriteLock();
                try
                {
                    _Items[index] = value;
                }
                finally
                {
                    _Lock.ExitWriteLock();
                }
            }
        }

        public int Count
        {
            get
            {
                _Lock.EnterReadLock();
                try
                {
                    return _Items.Count;
                }
                finally
                {
                    _Lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the ThreadSafeList class.
        /// </summary>
        public ThreadSafeList()
        {
            _Items = new List<T>();
            _Lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }

        public ThreadSafeList(IEnumerable<T> items)
        {
            _Items = new List<T>(items);
            _Lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }

        ~ThreadSafeList()
        {
            if (_Lock != null)
                _Lock.Dispose();
        }

        public void Add(T item)
        {
            _Lock.EnterWriteLock();
            try
            {
                _Items.Add(item);
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        public bool Remove(T item)
        {
            _Lock.EnterWriteLock();
            try
            {
                return _Items.Remove(item);
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        public int IndexOf(T item)
        {
            _Lock.EnterReadLock();
            try
            {
                return _Items.IndexOf(item);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        public void Clear()
        {
            _Lock.EnterWriteLock();
            try
            {
                _Items.Clear();
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        public bool Contains(T item)
        {
            _Lock.EnterReadLock();
            try
            {
                return _Items.Contains(item);
            }
            finally
            {
                _Lock.ExitReadLock();
            }

        }

        public T Find(Predicate<T> match)
        {
            _Lock.EnterReadLock();
            try
            {
                return _Items.Find(match);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        public int CountTS(Func<T, bool> predicate)
        {
            _Lock.EnterReadLock();
            try
            {
                return _Items.Count(predicate);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        public void Dispose()
        {
            if (_Lock != null)
                _Lock.Dispose();
            GC.SuppressFinalize(this);
        }

        public IEnumerator<T> GetEnumerator()
        {
            _Lock.EnterReadLock();
            try
            {
                return _Items.GetEnumerator();
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            _Lock.EnterReadLock();
            try
            {
                return _Items.GetEnumerator();
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        public System.Collections.ObjectModel.ReadOnlyCollection<T> AsReadOnly()
        {
            _Lock.EnterReadLock();
            List<T> cloneList = null;
            try
            {
                cloneList = new List<T>(_Items);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
            return cloneList.AsReadOnly();
        }

        public void ForEach(Action<T> predicate)
        {
            foreach (var item in this)
            {
                predicate(item);
            }
        }
    }

}
