using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary
{
    public sealed class PermissionDeniedException : Exception
    {
        public int StatusCode { get; }
        public PermissionDeniedException(int statusCode)
            : base(statusCode == 401 ? "Unauthorized" : "Forbidden")
        {
            StatusCode = statusCode;
        }
    }
}
