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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AutoFixture;
using FluentAssertions;

namespace MachineWatchTest
{
  public class ImmutableSpecimenBuilder : AutoFixture.Kernel.ISpecimenBuilder
  {
    public object Create(object request, AutoFixture.Kernel.ISpecimenContext context)
    {
      if (context == null)
        throw new ArgumentNullException(nameof(context));

      var t = request as Type;
      if (t == null)
        return new AutoFixture.Kernel.NoSpecimen();

      var args = t.GetGenericArguments();
      if (
        args.Length == 1
        && t.GetGenericTypeDefinition().FullName.StartsWith("System.Collections.Immutable.ImmutableList")
      )
      {
        var addRange = t.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
          )
          .Where(m =>
            m.Name == "AddRange"
            && m.GetParameters().Length == 1
            && m.GetParameters()[0]
              .ParameterType.FullName.StartsWith("System.Collections.Generic.IEnumerable")
          )
          .FirstOrDefault();

        var list = context.Resolve(addRange.GetParameters()[0].ParameterType);
        var im = t.GetField(
            "Empty",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
          )
          .GetValue(null);

        return addRange.Invoke(im, new[] { list });
      }
      else if (
        args.Length == 2
        && t.GetGenericTypeDefinition()
          .FullName.StartsWith("System.Collections.Immutable.ImmutableDictionary")
      )
      {
        var addRange = t.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
          )
          .Where(m =>
            m.Name == "AddRange"
            && m.GetParameters().Length == 1
            && m.GetParameters()[0]
              .ParameterType.FullName.StartsWith("System.Collections.Generic.IEnumerable")
          )
          .FirstOrDefault();

        var dictType = typeof(Dictionary<,>).MakeGenericType(args);
        var dict = context.Resolve(dictType);

        var emptyDict = t.GetField(
            "Empty",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
          )
          .GetValue(null);
        return addRange.Invoke(emptyDict, new[] { dict });
      }
      else
      {
        return new AutoFixture.Kernel.NoSpecimen();
      }
    }
  }

  public class InjectNullValuesForNullableTypesSpecimenBuilder : AutoFixture.Kernel.ISpecimenBuilder
  {
    private Random random = new Random();
    private const double LikelihoodOfNull = 0.1;

    public object Create(object request, AutoFixture.Kernel.ISpecimenContext context)
    {
      if (request is Type type)
      {
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
          if (random.NextDouble() < LikelihoodOfNull)
            return null;

          return context.Resolve(underlyingType);
        }
      }

      return new AutoFixture.Kernel.NoSpecimen();
    }
  }

  public class DateOnlySpecimenBuilder : AutoFixture.Kernel.ISpecimenBuilder
  {
    private readonly RandomNumericSequenceGenerator randomizer = new RandomNumericSequenceGenerator(
      DateOnly.FromDateTime(DateTime.Today).AddYears(-2).DayNumber,
      DateOnly.FromDateTime(DateTime.Today).AddYears(2).DayNumber
    );

    public object Create(object request, AutoFixture.Kernel.ISpecimenContext context)
    {
      var t = request as Type;
      if (t != null && t == typeof(DateOnly))
      {
        return DateOnly.FromDayNumber((int)randomizer.Create(typeof(int), context));
      }
      else
      {
        return new AutoFixture.Kernel.NoSpecimen();
      }
    }
  }

  public static class JobSpecimenBuilder
  {
    public static BlackMaple.MachineFramework.Job ClearCastingsOnLargerProcs(
      this BlackMaple.MachineFramework.Job j
    )
    {
      return j with
      {
        Processes = j
          .Processes.Select(p =>
            p with
            {
              Paths = p.Paths.Select(p => p with { Casting = null }).ToImmutableList()
            }
          )
          .ToImmutableList()
      };
    }
  }
}
