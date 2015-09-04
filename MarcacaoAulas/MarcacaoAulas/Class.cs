using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarcacaoAulas
{
    public class Class
    {
        public int Code { get; set; }

        public DateTime Time { get; set; }

        public AutoResetEvent BookCompleted { get; set; }

        public bool BookFired { get; set; }

        public Class()
        {
            BookCompleted = new AutoResetEvent(false);
            BookFired = false;
        }

        public override string ToString()
        {
            return string.Format("Class [{0}] at [{1}]", Code, Time.ToShortTimeString());
        }

    }
}
