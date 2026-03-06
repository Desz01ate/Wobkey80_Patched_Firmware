// Write decompilation output directly to file
// @category Analysis

import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.FunctionManager;
import java.io.FileWriter;
import java.io.PrintWriter;

public class GhidraDeep extends GhidraScript {

    @Override
    public void run() throws Exception {
        PrintWriter out = new PrintWriter(new FileWriter("/tmp/ghidra_decomp.txt"));
        DecompInterface decomp = new DecompInterface();
        decomp.openProgram(currentProgram);
        FunctionManager fm = currentProgram.getFunctionManager();

        // Disassemble gap area
        for (long addr = 0x0D500L; addr < 0x0DE00L; addr += 2) {
            disassemble(toAddr(addr));
        }
        for (long addr = 0x0D500L; addr < 0x0DE00L; addr += 2) {
            try {
                if (fm.getFunctionContaining(toAddr(addr)) == null) {
                    createFunction(toAddr(addr), null);
                }
            } catch (Exception e) {}
        }

        // Report 0x0DC9C location
        Function efFn = fm.getFunctionContaining(toAddr(0x0DC9CL));
        if (efFn != null) {
            out.printf("*** 0x0DC9C in %s at 0x%X size=%d ***\n\n",
                       efFn.getName(), efFn.getEntryPoint().getOffset(),
                       efFn.getBody().getNumAddresses());
        }

        // Decompile all functions > 40 bytes
        FunctionIterator funcs = fm.getFunctions(true);
        while (funcs.hasNext()) {
            Function fn = funcs.next();
            long size = fn.getBody().getNumAddresses();
            if (size <= 40) continue;

            long entry = fn.getEntryPoint().getOffset();
            DecompileResults result = decomp.decompileFunction(fn, 60, null);
            if (result == null || !result.decompileCompleted() || result.getDecompiledFunction() == null) continue;
            String code = result.getDecompiledFunction().getC();

            // Print ALL functions in 0xC000-0xE000 range (VIA handler area)
            // and any large function or one with VIA-relevant patterns
            boolean inRange = entry >= 0x0C000L && entry <= 0x0E000L;
            boolean hasVIA = code.contains("0x12") || code.contains("== 7") ||
                             code.contains("== 8") || code.contains("switch") || size > 300;

            if (inRange || hasVIA) {
                out.printf("\n%s\n", "=".repeat(72));
                out.printf("0x%05X  %s  size=%d\n", entry, fn.getName(), size);
                out.printf("%s\n", "=".repeat(72));
                out.println(code);
            }
        }

        decomp.dispose();
        out.println("\n--- Done ---");
        out.close();
        println("Output written to /tmp/ghidra_decomp.txt");
    }
}
