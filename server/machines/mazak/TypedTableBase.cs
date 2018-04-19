#if NETCOREAPP2_0
using System.Collections;
using System.Linq;

namespace System.Data
{
    public abstract class TypedTableBase<T>
        : System.Data.DataTable, System.Collections.Generic.IEnumerable<T>
        where T : System.Data.DataRow
    {

        protected TypedTableBase() : base() {}
        protected TypedTableBase(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
            : base(info, context) {}

        System.Collections.Generic.IEnumerator<T> System.Collections.Generic.IEnumerable<T>.GetEnumerator() {
            return Rows.Cast<T>().GetEnumerator();
        }
        public System.Collections.IEnumerator GetEnumeratorBasic() {
            return Rows.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return Rows.GetEnumerator();
        }
  }
}
#endif