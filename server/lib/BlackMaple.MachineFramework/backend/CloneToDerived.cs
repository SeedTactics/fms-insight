/* Copyright (c) 2021, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Linq;
using System.Linq.Expressions;

namespace BlackMaple.MachineFramework
{
  public static class CloneToDerivedExtension
  {
    public static R CloneToDerived<R, T>(this T val)
      where R : T
    {
      return CloneHelper<R, T>.CloneToDerived(val);
    }

    private static class CloneHelper<R, T>
    {
      public static readonly Func<T, R> CloneToDerived;

      static CloneHelper()
      {
        ParameterExpression val = Expression.Variable(typeof(T), "val");
        var ctr = typeof(R).GetConstructors().First(c => !c.GetParameters().Any());

        var bindings = typeof(T)
          .GetProperties()
          .Where(p =>
            p.GetMethod != null && p.GetMethod.IsPublic && p.SetMethod != null && p.SetMethod.IsPublic
          )
          .Select(prop => Expression.Bind(typeof(R).GetProperty(prop.Name)!, Expression.Property(val, prop)))
          .ToArray();

        var mbrInit = Expression.MemberInit(Expression.New(ctr), bindings);

        CloneToDerived = Expression.Lambda<Func<T, R>>(mbrInit, new[] { val }).Compile();
      }
    }
  }
}
