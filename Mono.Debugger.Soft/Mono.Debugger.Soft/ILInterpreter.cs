using System;
using Mono.Cecil.Cil;

namespace Mono.Debugger.Soft
{
    internal class ILInterpreter
    {
        readonly MethodMirror method;
        readonly AppDomainMirror appDomain;

        public ILInterpreter(MethodMirror method)
        {
            this.method = method;
            this.appDomain = method.DeclaringType.Assembly.Domain;
        }

        public Value Evaluate(Value this_val, Value[] args)
        {
            var body = method.GetMethodBody();

            // Implement only the IL opcodes required to evaluate mcs compiled property accessors:
            // IL_0000:  nop
            // IL_0001:  ldarg.0
            // IL_0002:  ldfld      int32 Tests::field_i
            // IL_0007:  stloc.0
            // IL_0008:  br         IL_000d
            // IL_000d:  ldloc.0
            // IL_000e:  ret
            // ... or returns a simple constant:
            // IL_0000:  ldc.i4 1024
            // IL_0005:  conv.i8
            // IL_0006:  ret
            if (args != null && args.Length != 0)
                throw new NotSupportedException();

            //If method is virtual we can't optimize(execute IL) because it's maybe
            //overriden... call runtime to invoke overriden version...
            if (method.IsVirtual)
                throw new NotSupportedException();

            if (method.IsStatic || method.DeclaringType.IsValueType || this_val == null || !(this_val is ObjectMirror))
                throw new NotSupportedException();

            var instructions = body.Instructions;
            if (instructions.Count < 1 || instructions.Count > 16)
                throw new NotSupportedException();

            var stack = new Value [16];
            var ins = instructions[0];
            Value locals_0 = null;
            int ins_count = 0;
            int sp = 0;

            while (ins != null)
            {
                if (ins_count > 16)
                    throw new NotImplementedException();

                var next = ins.Next;
                ins_count++;

                var op = ins.OpCode;
                if (op == OpCodes.Nop) { }
                else if (op == OpCodes.Ldarg_0)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    stack[sp++] = this_val;
                }
                else if (op == OpCodes.Ldfld)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    var obj = (ObjectMirror)stack[--sp];
                    var field = (FieldInfoMirror)ins.Operand;
                    try
                    {
                        stack[sp++] = obj.GetValue(field);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_0)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(0, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_1)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(1, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_2)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(2, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_3)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(3, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_4)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(4, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_5)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(5, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_6)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(6, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_7)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(7, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_8)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(8, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_M1)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(-1, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(ins.Operand, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I4_S)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(ins.Operand, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_I8)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(ins.Operand, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_R4)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(ins.Operand, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Ldc_R8)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    try
                    {
                        stack[sp++] = new PrimitiveValue(ins.Operand, appDomain);
                    }
                    catch (ArgumentException)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_I)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToInt32(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_I1)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToSByte(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_U1)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToByte(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_I2)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToInt16(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_U2)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToUInt16(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_I4)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToInt32(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_U4)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToUInt32(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_I8)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToInt64(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_U8)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToUInt64(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_R4)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToSingle(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Conv_R8)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    try
                    {
                        var primitive = (PrimitiveValue)stack[--sp];
                        stack[sp++] = new PrimitiveValue(Convert.ToDouble(primitive.Value), appDomain);
                    }
                    catch
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (op == OpCodes.Stloc_0)
                {
                    if (sp != 1)
                        throw new NotSupportedException();

                    locals_0 = stack[--sp];
                }
                else if (op == OpCodes.Br || op == OpCodes.Br_S)
                {
                    next = (ILInstruction)ins.Operand;
                }
                else if (op == OpCodes.Ldloc_0)
                {
                    if (sp != 0)
                        throw new NotSupportedException();

                    stack[sp++] = locals_0;
                }
                else if (op == OpCodes.Ret)
                {
                    if (sp > 0)
                    {
                        var res = stack[--sp];

                        var primitive = res as PrimitiveValue;
                        if (method.ReturnType.IsPrimitive && primitive != null)
                        {
                            // cast the primitive value to the return type
                            try
                            {
                                switch (method.ReturnType.CSharpName)
                                {
                                    case "double":
                                        res = new PrimitiveValue(Convert.ToDouble(primitive.Value), appDomain);
                                        break;
                                    case "float":
                                        res = new PrimitiveValue(Convert.ToSingle(primitive.Value), appDomain);
                                        break;
                                    case "ulong":
                                        res = new PrimitiveValue(Convert.ToUInt64(primitive.Value), appDomain);
                                        break;
                                    case "long":
                                        res = new PrimitiveValue(Convert.ToInt64(primitive.Value), appDomain);
                                        break;
                                    case "uint":
                                        res = new PrimitiveValue(Convert.ToUInt32(primitive.Value), appDomain);
                                        break;
                                    case "int":
                                        res = new PrimitiveValue(Convert.ToInt32(primitive.Value), appDomain);
                                        break;
                                    case "ushort":
                                        res = new PrimitiveValue(Convert.ToUInt16(primitive.Value), appDomain);
                                        break;
                                    case "short":
                                        res = new PrimitiveValue(Convert.ToInt16(primitive.Value), appDomain);
                                        break;
                                    case "sbyte":
                                        res = new PrimitiveValue(Convert.ToSByte(primitive.Value), appDomain);
                                        break;
                                    case "byte":
                                        res = new PrimitiveValue(Convert.ToByte(primitive.Value), appDomain);
                                        break;
                                    case "char":
                                        res = new PrimitiveValue(Convert.ToChar(primitive.Value), appDomain);
                                        break;
                                    case "bool":
                                        res = new PrimitiveValue(Convert.ToBoolean(primitive.Value), appDomain);
                                        break;
                                }
                            }
                            catch
                            {
                                throw new NotSupportedException();
                            }
                        }
                        else if (method.ReturnType.IsEnum && primitive != null)
                        {
                            try
                            {
                                res = method.VirtualMachine.CreateEnumMirror(method.ReturnType, primitive);
                            }
                            catch
                            {
                                throw new NotSupportedException();
                            }
                        }

                        return res;
                    }

                    return null;
                }
                else
                {
                    throw new NotSupportedException();
                }

                ins = next;
            }

            return null;
        }
    }
}
