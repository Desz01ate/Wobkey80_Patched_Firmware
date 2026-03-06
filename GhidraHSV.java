// Decompile all functions in LED area including small ones
// @category Analysis

import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.FunctionManager;
import java.io.FileWriter;
import java.io.PrintWriter;

public class GhidraHSV extends GhidraScript {

    @Override
    public void run() throws Exception {
        PrintWriter out = new PrintWriter(new FileWriter("/tmp/ghidra_hsv.txt"));
        DecompInterface decomp = new DecompInterface();
        decomp.openProgram(currentProgram);
        FunctionManager fm = currentProgram.getFunctionManager();

        // Force create functions in C000-E000
        for (long addr = 0x0C000L; addr < 0x0E000L; addr += 2) {
            disassemble(toAddr(addr));
        }
        for (long addr = 0x0C000L; addr < 0x0E000L; addr += 2) {
            try {
                if (fm.getFunctionContaining(toAddr(addr)) == null) {
                    createFunction(toAddr(addr), null);
                }
            } catch (Exception e) {}
        }

        // Decompile ALL functions, filter to C000-E000 range
        out.println("=== ALL functions in 0xC000-0xE000 ===");
        FunctionIterator funcs = fm.getFunctions(true);
        while (funcs.hasNext()) {
            Function fn = funcs.next();
            long entry = fn.getEntryPoint().getOffset();
            if (entry < 0x0C000L || entry >= 0x0E000L) continue;
            long size = fn.getBody().getNumAddresses();
            DecompileResults result = decomp.decompileFunction(fn, 60, null);
            if (result != null && result.decompileCompleted() && result.getDecompiledFunction() != null) {
                String code = result.getDecompiledFunction().getC();
                out.printf("\n========================================================================\n");
                out.printf("0x%05X  %s  size=%d\n", entry, fn.getName(), size);
                out.printf("========================================================================\n");
                out.println(code);
            } else {
                out.printf("\n--- 0x%05X  %s  size=%d  DECOMPILE FAILED ---\n", entry, fn.getName(), size);
            }
        }

        // Also decompile function containing 0xCFD8
        out.println("\n=== Function containing 0xCFD8 ===");
        Function cfn = fm.getFunctionContaining(toAddr(0x0CFD8L));
        if (cfn != null) {
            long entry = cfn.getEntryPoint().getOffset();
            out.printf("0xCFD8 is in %s at 0x%X size=%d\n", cfn.getName(), entry, cfn.getBody().getNumAddresses());
            DecompileResults result = decomp.decompileFunction(cfn, 60, null);
            if (result != null && result.decompileCompleted() && result.getDecompiledFunction() != null) {
                out.println(result.getDecompiledFunction().getC());
            }
        } else {
            out.println("No function at 0xCFD8");
        }

        decomp.dispose();
        out.println("\n--- HSV analysis complete ---");
        out.close();
        println("Output written to /tmp/ghidra_hsv.txt");
    }
}
