using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Threading;
namespace NetDownloader.Collections
{
    [DebuggerDisplay("Count = {Count}"), ComVisible(false)]
    [HostProtection(SecurityAction.LinkDemand, Synchronization = true, ExternalThreading = true)]
    [Serializable]
    internal class ConcurrentQueueEx<T> : IProducerConsumerCollection<T>, IEnumerable<T>, ICollection, IEnumerable, INotifyCollectionChanged
    {
        private class Segment
        {
            internal volatile T[] m_array;

            internal volatile VolatileBool[] m_state;

            private volatile ConcurrentQueueEx<T>.Segment m_next;

            internal readonly long m_index;

            private /*volatile*/ int m_low;

            private /*volatile*/ int m_high;

            private volatile ConcurrentQueueEx<T> m_source;

            internal ConcurrentQueueEx<T>.Segment Next
            {
                get
                {
                    return m_next;
                }
            }

            internal bool IsEmpty
            {
                get
                {
                    return Low > High;
                }
            }

            internal int Low
            {
                get
                {
                    return Math.Min(m_low, 32);
                }
            }

            internal int High
            {
                get
                {
                    return Math.Min(m_high, 31);
                }
            }

            internal Segment(long index, ConcurrentQueueEx<T> source)
            {
                m_array = new T[32];
                m_state = new VolatileBool[32];
                m_high = -1;
                m_index = index;
                m_source = source;
            }

            internal void UnsafeAdd(T value)
            {
                m_high++;
                m_array[m_high] = value;
                m_state[m_high].m_value = true;
            }

            internal ConcurrentQueueEx<T>.Segment UnsafeGrow()
            {
                ConcurrentQueueEx<T>.Segment segment = new ConcurrentQueueEx<T>.Segment(m_index + 1L, m_source);
                m_next = segment;
                return segment;
            }

            internal void Grow()
            {
                ConcurrentQueueEx<T>.Segment next = new ConcurrentQueueEx<T>.Segment(m_index + 1L, m_source);
                m_next = next;
                m_source.m_tail = m_next;
            }

            internal bool TryAppend(T value)
            {
                if (m_high >= 31)
                {
                    return false;
                }
                int num = 32;
                try
                {
                }
                finally
                {
                    num = Interlocked.Increment(ref m_high);
                    if (num <= 31)
                    {
                        m_array[num] = value;
                        m_state[num].m_value = true;
                    }
                    if (num == 31)
                    {
                        Grow();
                    }
                }
                return num <= 31;
            }

            internal bool TryRemove(out T result)
            {
                SpinWait spinWait = default(SpinWait);
                int i = Low;
                int high = High;
                while (i <= high)
                {
                    if (Interlocked.CompareExchange(ref m_low, i + 1, i) == i)
                    {
                        SpinWait spinWait2 = default(SpinWait);
                        while (!m_state[i].m_value)
                        {
                            spinWait2.SpinOnce();
                        }
                        result = m_array[i];
                        if (m_source.m_numSnapshotTakers <= 0)
                        {
                            m_array[i] = default(T);
                        }
                        if (i + 1 >= 32)
                        {
                            spinWait2 = default(SpinWait);
                            while (m_next == null)
                            {
                                spinWait2.SpinOnce();
                            }
                            m_source.m_head = m_next;
                        }
                        return true;
                    }
                    spinWait.SpinOnce();
                    i = Low;
                    high = High;
                }
                result = default(T);
                return false;
            }

            internal bool TryPeek(out T result)
            {
                result = default(T);
                int low = Low;
                if (low > High)
                {
                    return false;
                }
                SpinWait spinWait = default(SpinWait);
                while (!m_state[low].m_value)
                {
                    spinWait.SpinOnce();
                }
                result = m_array[low];
                return true;
            }

            internal void AddToList(List<T> list, int start, int end)
            {
                for (int i = start; i <= end; i++)
                {
                    SpinWait spinWait = default(SpinWait);
                    while (!m_state[i].m_value)
                    {
                        spinWait.SpinOnce();
                    }
                    list.Add(m_array[i]);
                }
            }
        }

        [NonSerialized]
        private volatile ConcurrentQueueEx<T>.Segment m_head;

        [NonSerialized]
        private volatile ConcurrentQueueEx<T>.Segment m_tail;

        private T[] m_serializationArray;
        private readonly object InitFromListLock = new object();

        [NonSerialized]
        internal /*volatile*/ int m_numSnapshotTakers;

        private bool InternalChange = false;

        private const int SEGMENT_SIZE = 32;

        /// <summary>Gets a value indicating whether access to the <see cref="T:System.Collections.ICollection" /> is synchronized with the SyncRoot.</summary>
        /// <returns>true if access to the <see cref="T:System.Collections.ICollection" /> is synchronized with the SyncRoot; otherwise, false. For <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />, this property always returns false.</returns>

        bool ICollection.IsSynchronized
        {

            get
            {
                return false;
            }
        }

        /// <summary>Gets an object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection" />. This property is not supported.</summary>
        /// <returns>Returns null  (Nothing in Visual Basic).</returns>
        /// <exception cref="T:System.NotSupportedException">The SyncRoot property is not supported.</exception>

        object ICollection.SyncRoot
        {

            get
            {
                throw new NotSupportedException("ConcurrentCollection_SyncRoot_NotSupported");
            }
        }

        /// <summary>Gets a value that indicates whether the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" /> is empty.</summary>
        /// <returns>true if the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" /> is empty; otherwise, false.</returns>

        public bool IsEmpty
        {

            get
            {
                ConcurrentQueueEx<T>.Segment head;
                lock (InitFromListLock)
                {
                    head = m_head;
                }
                if (!head.IsEmpty)
                {
                    return false;
                }
                if (head.Next == null)
                {
                    return true;
                }
                SpinWait spinWait = default(SpinWait);
                while (head.IsEmpty)
                {
                    if (head.Next == null)
                    {
                        return true;
                    }
                    spinWait.SpinOnce();
                    head = m_head;
                }
                return false;
            }
        }

        /// <summary>Gets the number of elements contained in the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />.</summary>
        /// <returns>The number of elements contained in the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />.</returns>

        public int Count
        {

            get
            {
                ConcurrentQueueEx<T>.Segment segment;
                ConcurrentQueueEx<T>.Segment segment2;
                int num;
                int num2;
                GetHeadTailPositions(out segment, out segment2, out num, out num2);
                if (segment == segment2)
                {
                    return num2 - num + 1;
                }
                int num3 = 32 - num;
                num3 += 32 * (int)(segment2.m_index - segment.m_index - 1L);
                return num3 + (num2 + 1);
            }
        }

        public virtual event NotifyCollectionChangedEventHandler CollectionChanged;

        /// <summary>Initializes a new instance of the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" /> class.</summary>
        public ConcurrentQueueEx()
        {
            m_head = (m_tail = new ConcurrentQueueEx<T>.Segment(0L, this));
        }

        private void InitializeFromCollection(IEnumerable<T> collection)
        {
            lock (InitFromListLock)
            {
                ConcurrentQueueEx<T>.Segment segment = new ConcurrentQueueEx<T>.Segment(0L, this);
                m_head = segment;
                int num = 0;
                foreach (T current in collection)
                {
                    segment.UnsafeAdd(current);
                    num++;
                    if (num >= 32)
                    {
                        segment = segment.UnsafeGrow();
                        num = 0;
                    }
                }
                m_tail = segment;
            }
        }

        /// <summary>Initializes a new instance of the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" /> class that contains elements copied from the specified collection</summary>
        /// <param name="collection">The collection whose elements are copied to the new <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />.</param>
        /// <exception cref="T:System.ArgumentNullException">The <paramref name="collection" /> argument is null.</exception>

        public ConcurrentQueueEx(IEnumerable<T> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException("collection");
            }
            InitializeFromCollection(collection);
        }

        [OnSerializing]
        private void OnSerializing(StreamingContext context)
        {
            m_serializationArray = ToArray();
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            InitializeFromCollection(m_serializationArray);
            m_serializationArray = null;
        }

        /// <summary>Copies the elements of the <see cref="T:System.Collections.ICollection" /> to an <see cref="T:System.Array" />, starting at a particular <see cref="T:System.Array" /> index.</summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied from the <see cref="T:System.Collections.Concurrent.ConcurrentBag" />. The <see cref="T:System.Array" /> must have zero-based indexing.</param>
        /// <param name="index">The zero-based index in <paramref name="array" /> at which copying begins.</param>
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="array" /> is a null reference (Nothing in Visual Basic).</exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        ///   <paramref name="index" /> is less than zero.</exception>
        /// <exception cref="T:System.ArgumentException">
        ///   <paramref name="array" /> is multidimensional. -or- <paramref name="array" /> does not have zero-based indexing. -or- <paramref name="index" /> is equal to or greater than the length of the <paramref name="array" /> -or- The number of elements in the source <see cref="T:System.Collections.ICollection" /> is greater than the available space from <paramref name="index" /> to the end of the destination <paramref name="array" />. -or- The type of the source <see cref="T:System.Collections.ICollection" /> cannot be cast automatically to the type of the destination <paramref name="array" />.</exception>

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }
            ((ICollection)ToList()).CopyTo(array, index);
        }

        /// <summary>Returns an enumerator that iterates through a collection.</summary>
        /// <returns>An <see cref="T:System.Collections.IEnumerator" /> that can be used to iterate through the collection.</returns>

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<T>)this).GetEnumerator();
        }


        bool IProducerConsumerCollection<T>.TryAdd(T item)
        {
            Enqueue(item);
            return true;
        }

        [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")]
        bool IProducerConsumerCollection<T>.TryTake(out T item)
        {
            return TryDequeue(out item);
        }

        /// <summary>Copies the elements stored in the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" /> to a new array.</summary>
        /// <returns>A new array containing a snapshot of elements copied from the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />.</returns>

        public T[] ToArray()
        {
            return ToList().ToArray();
        }

        private List<T> ToList()
        {
            Interlocked.Increment(ref m_numSnapshotTakers);
            List<T> list = new List<T>();
            try
            {
                ConcurrentQueueEx<T>.Segment segment;
                ConcurrentQueueEx<T>.Segment segment2;
                int start;
                int end;
                GetHeadTailPositions(out segment, out segment2, out start, out end);
                if (segment == segment2)
                {
                    segment.AddToList(list, start, end);
                }
                else
                {
                    segment.AddToList(list, start, 31);
                    for (ConcurrentQueueEx<T>.Segment next = segment.Next; next != segment2; next = next.Next)
                    {
                        next.AddToList(list, 0, 31);
                    }
                    segment2.AddToList(list, 0, end);
                }
            }
            finally
            {
                Interlocked.Decrement(ref m_numSnapshotTakers);
            }
            return list;
        }

        public System.Collections.ObjectModel.ReadOnlyCollection<T> AsReadonly()
        {
            return ToList().AsReadOnly();
        }

        private void GetHeadTailPositions(out ConcurrentQueueEx<T>.Segment head, out ConcurrentQueueEx<T>.Segment tail, out int headLow, out int tailHigh)
        {
            head = m_head;
            tail = m_tail;
            headLow = head.Low;
            tailHigh = tail.High;
            SpinWait spinWait = default(SpinWait);
            while (head != m_head || tail != m_tail || headLow != head.Low || tailHigh != tail.High || head.m_index > tail.m_index)
            {
                spinWait.SpinOnce();
                head = m_head;
                tail = m_tail;
                headLow = head.Low;
                tailHigh = tail.High;
            }
        }

        /// <summary>Copies the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" /> elements to an existing one-dimensional <see cref="T:System.Array" />, starting at the specified array index.</summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied from the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />. The <see cref="T:System.Array" /> must have zero-based indexing.</param>
        /// <param name="index">The zero-based index in <paramref name="array" /> at which copying begins.</param>
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="array" /> is a null reference (Nothing in Visual Basic).</exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        ///   <paramref name="index" /> is less than zero.</exception>
        /// <exception cref="T:System.ArgumentException">
        ///   <paramref name="index" /> is equal to or greater than the length of the <paramref name="array" /> -or- The number of elements in the source <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" /> is greater than the available space from <paramref name="index" /> to the end of the destination <paramref name="array" />.</exception>

        public void CopyTo(T[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }
            ToList().CopyTo(array, index);
        }

        /// <summary>Returns an enumerator that iterates through the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />.</summary>
        /// <returns>An enumerator for the contents of the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />.</returns>

        public IEnumerator<T> GetEnumerator()
        {
            Interlocked.Increment(ref m_numSnapshotTakers);
            ConcurrentQueueEx<T>.Segment head;
            ConcurrentQueueEx<T>.Segment tail;
            int headLow;
            int tailHigh;
            GetHeadTailPositions(out head, out tail, out headLow, out tailHigh);
            return GetEnumerator(head, tail, headLow, tailHigh);
        }

        private IEnumerator<T> GetEnumerator(ConcurrentQueueEx<T>.Segment head, ConcurrentQueueEx<T>.Segment tail, int headLow, int tailHigh)
        {
            try
            {
                SpinWait spinWait = default(SpinWait);
                if (head == tail)
                {
                    for (int i = headLow; i <= tailHigh; i++)
                    {
                        spinWait.Reset();
                        while (!head.m_state[i].m_value)
                        {
                            spinWait.SpinOnce();
                        }
                        yield return head.m_array[i];
                    }
                }
                else
                {
                    for (int j = headLow; j < 32; j++)
                    {
                        spinWait.Reset();
                        while (!head.m_state[j].m_value)
                        {
                            spinWait.SpinOnce();
                        }
                        yield return head.m_array[j];
                    }
                    for (ConcurrentQueueEx<T>.Segment next = head.Next; next != tail; next = next.Next)
                    {
                        for (int k = 0; k < 32; k++)
                        {
                            spinWait.Reset();
                            while (!next.m_state[k].m_value)
                            {
                                spinWait.SpinOnce();
                            }
                            yield return next.m_array[k];
                        }
                    }
                    for (int l = 0; l <= tailHigh; l++)
                    {
                        spinWait.Reset();
                        while (!tail.m_state[l].m_value)
                        {
                            spinWait.SpinOnce();
                        }
                        yield return tail.m_array[l];
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref m_numSnapshotTakers);
            }
            yield break;
        }

        /// <summary>Adds an object to the end of the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />.</summary>
        /// <param name="item">The object to add to the end of the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />. The value can be a null reference (Nothing in Visual Basic) for reference types.</param>

        public void Enqueue(T item)
        {
            SpinWait spinWait = default(SpinWait);
            while (true)
            {
                ConcurrentQueueEx<T>.Segment tail = m_tail;
                if (tail.TryAppend(item))
                {
                    if (!InternalChange)
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, 0));
                    break;
                }
                spinWait.SpinOnce();
            }
        }

        /// <summary>Attempts to remove and return the object at the beginning of the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" />.</summary>
        /// <returns>true if an element was removed and returned from the beggining of the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" /> succesfully; otherwise, false.</returns>
        /// <param name="result">When this method returns, if the operation was successful, <paramref name="result" /> contains the object removed. If no object was available to be removed, the value is unspecified.</param>

        public bool TryDequeue(out T result)
        {
            while (!IsEmpty)
            {
                ConcurrentQueueEx<T>.Segment head = m_head;
                if (head.TryRemove(out result))
                {
                    if (!InternalChange)
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, result, 0));
                    return true;
                }
            }
            result = default(T);
            return false;
        }

        public bool TryRemove(T item)
        {
            Interlocked.Increment(ref m_numSnapshotTakers);
            try
            {
                var myList = ToList();
                if (myList.Contains(item))
                {
                    myList.Remove(item);
                    if (myList.Count == 0)
                        m_head = (m_tail = new ConcurrentQueueEx<T>.Segment(0L, this));
                    else
                        m_head = m_tail = null;
                    InitializeFromCollection(myList);
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
                    return true;
                }
                return false;
            }
            finally
            {
                Interlocked.Decrement(ref m_numSnapshotTakers);
            }
        }

        public void TryRemove(IEnumerable<T> items)
        {
            Interlocked.Increment(ref m_numSnapshotTakers);
            try
            {
                var myList = ToList();
                var removedItems = new List<T>();
                foreach (T item in items)
                {
                    if (myList.Contains(item))
                    {
                        myList.Remove(item);
                        removedItems.Add(item);
                    }
                }
                if (removedItems.Count > 0)
                {
                    m_head = m_tail = null;
                    InitializeFromCollection(myList);
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removedItems));
                }
            }
            finally
            {
                Interlocked.Decrement(ref m_numSnapshotTakers);
            }
        }

        /// <summary>Attempts to return an object from the beginning of the <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1" /> without removing it.</summary>
        /// <returns>true if and object was returned successfully; otherwise, false.</returns>
        /// <param name="result">When this method returns, <paramref name="result" /> contains an object from the beginning of the <see cref="T:System.Collections.Concurrent.ConccurrentQueue{T}" /> or an unspecified value if the operation failed.</param>

        public bool TryPeek(out T result)
        {
            Interlocked.Increment(ref m_numSnapshotTakers);
            while (!IsEmpty)
            {
                ConcurrentQueueEx<T>.Segment head = m_head;
                if (head.TryPeek(out result))
                {
                    Interlocked.Decrement(ref m_numSnapshotTakers);
                    return true;
                }
            }
            result = default(T);
            Interlocked.Decrement(ref m_numSnapshotTakers);
            return false;
        }

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            var handler = CollectionChanged;
            if (handler != null)
            {
                handler.BeginInvoke(this, e, EndAsyncEvent, null);
            }
        }

        private void EndAsyncEvent(IAsyncResult iar)
        {
            var ar = (System.Runtime.Remoting.Messaging.AsyncResult)iar;
            var invokedMethod = (NotifyCollectionChangedEventHandler)ar.AsyncDelegate;
            try
            {
                invokedMethod.EndInvoke(iar);
            }
            catch { }
        }

        public void Clear()
        {
            InternalChange = true;
            while (!IsEmpty)
            {
                T item = default(T);
                TryDequeue(out item);
            }
            InternalChange = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
