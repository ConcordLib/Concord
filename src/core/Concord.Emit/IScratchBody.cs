using System.Reflection;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Emit;

/// <summary>
///     The method body a backend opened through <see cref="IScratchGenerator.Begin" />, written once
///     in emit order: locals, instructions, clauses, <see cref="Finish" />. Operands are BCL types.
///     Disposing it releases whatever the backend held, on the refusal path as well as the finished one.
/// </summary>
public interface IScratchBody : IDisposable {
    /// <summary>Sets the header's <c>init</c> flag, which zero-initializes the local slots.</summary>
    /// <param name="value">Whether the slots are zero-initialized.</param>
    void InitLocals(bool value);

    /// <summary>Declares the next local slot.</summary>
    /// <param name="type">The slot's declared type, with any pin already unwrapped.</param>
    /// <param name="pinned">Whether the slot is pinned.</param>
    /// <returns>The slot number, which counts up from zero in declaration order.</returns>
    int DeclareLocal(Type type, bool pinned);

    /// <summary>Points a label at the instruction emitted next.</summary>
    /// <param name="label">The label id.</param>
    void MarkLabel(int label);

    /// <summary>Emits an instruction that takes no operand.</summary>
    /// <param name="opcode">The opcode to emit.</param>
    void Emit(OpCode opcode);

    /// <summary>
    ///     Emits an instruction whose operand is a local slot, an argument index or an integer
    ///     literal. Which of the three it is follows from <paramref name="opcode" />.
    /// </summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The slot, index or literal.</param>
    void Emit(OpCode opcode, int operand);

    /// <summary>Emits an instruction that takes a 64-bit integer literal.</summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The literal.</param>
    void Emit(OpCode opcode, long operand);

    /// <summary>Emits an instruction that takes a single-precision literal.</summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The literal.</param>
    void Emit(OpCode opcode, float operand);

    /// <summary>Emits an instruction that takes a double-precision literal.</summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The literal.</param>
    void Emit(OpCode opcode, double operand);

    /// <summary>Emits an instruction that takes a string literal.</summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The literal.</param>
    void Emit(OpCode opcode, string operand);

    /// <summary>Emits an instruction that takes a type.</summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The type the instruction names.</param>
    void Emit(OpCode opcode, Type operand);

    /// <summary>Emits an instruction that takes a field.</summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The field the instruction names.</param>
    void Emit(OpCode opcode, FieldInfo operand);

    /// <summary>Emits an instruction that takes a method or a constructor.</summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The method the instruction calls or loads.</param>
    void Emit(OpCode opcode, MethodBase operand);

    /// <summary>Emits a branch to a label, which may be marked later.</summary>
    /// <param name="opcode">The branch opcode.</param>
    /// <param name="label">The label id the branch targets.</param>
    void EmitBranch(OpCode opcode, int label);

    /// <summary>Emits a <c>switch</c> over a jump table of labels, which may be marked later.</summary>
    /// <param name="labels">The label ids the table targets, in order.</param>
    void EmitSwitch(int[] labels);

    /// <summary>Emits an indirect call through a call-site signature.</summary>
    /// <param name="opcode">The call opcode.</param>
    /// <param name="returnType">The type the call site returns.</param>
    /// <param name="parameterTypes">The types the call site takes, in order.</param>
    /// <param name="conventionCode">The calling convention as its raw signature byte.</param>
    /// <param name="hasThis">Whether the call site takes an instance as its first argument.</param>
    /// <param name="explicitThis">Whether that instance argument is spelled out in the signature.</param>
    void EmitCalli(OpCode opcode, Type returnType, Type[] parameterTypes, int conventionCode, bool hasThis, bool explicitThis);

    /// <summary>
    ///     Adds one exception-handling region. Every index counts instructions in emit order, and
    ///     every end index is exclusive.
    /// </summary>
    /// <param name="kind">Which kind of handler this is.</param>
    /// <param name="tryStart">The first instruction of the protected region.</param>
    /// <param name="tryEnd">One past the last instruction of the protected region.</param>
    /// <param name="handlerStart">The first instruction of the handler.</param>
    /// <param name="handlerEnd">One past the last instruction of the handler, or the instruction count when it runs to the end of the method.</param>
    /// <param name="catchType">The caught type on a catch clause, otherwise null.</param>
    /// <param name="filterStart">The first instruction of the filter on a filter clause, otherwise -1.</param>
    void AddClause(ClauseKind kind, int tryStart, int tryEnd, int handlerStart, int handlerEnd, Type? catchType, int filterStart);

    /// <summary>Closes the body and emits it.</summary>
    /// <returns>A method that runs what was written.</returns>
    MethodInfo Finish();
}
