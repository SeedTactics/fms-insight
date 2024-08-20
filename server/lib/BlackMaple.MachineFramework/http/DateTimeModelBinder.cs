/* Copyright (c) 2018, John Lenz

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
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace BlackMaple.MachineFramework
{
  public class DateTimeModelBinder : IModelBinder
  {
    static readonly string[] dateFormats =
    {
      "yyyyMMddTHHmmssZ",
      "yyyy-MM-ddTHH:mm:ssZ",
      "yyyy-MM-ddTHH:mm:ss.FFFZ",
    };

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
      if (bindingContext == null)
        throw new ArgumentNullException(nameof(bindingContext));
      var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
      if (valueProviderResult == ValueProviderResult.None)
        return Task.CompletedTask;

      bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);

      var dateStr = valueProviderResult.FirstValue;
      if (
        DateTime.TryParseExact(
          dateStr,
          dateFormats,
          System.Globalization.CultureInfo.InvariantCulture,
          System.Globalization.DateTimeStyles.RoundtripKind,
          out DateTime date
        )
      )
      {
        bindingContext.Result = ModelBindingResult.Success(date);
      }
      else
      {
        bindingContext.ModelState.TryAddModelError(
          bindingContext.ModelName,
          "DateTime should be in formatted in ISO-8601 UTC datetime format"
        );
      }

      return Task.CompletedTask;
    }
  }

  public class DateTimeBinderProvider : IModelBinderProvider
  {
    public IModelBinder GetBinder(ModelBinderProviderContext context)
    {
      if (context == null)
      {
        throw new ArgumentNullException(nameof(context));
      }

      if (context.Metadata.ModelType == typeof(DateTime))
      {
        return new BinderTypeModelBinder(typeof(DateTimeModelBinder));
      }

      return null;
    }
  }
}
