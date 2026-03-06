// Ghidra headless script to analyze Wobkey Crush 80 firmware
// Finds VIA protocol handler and RGB color/hue logic
// @category Analysis

import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.FunctionManager;

public class GhidraAnalyze extends GhidraScript {

    @Override
    public void run() throws Exception {
        DecompInterface decomp = new DecompInterface();
        decomp.openProgram(currentProgram);

        FunctionManager fm = currentProgram.getFunctionManager();
        FunctionIterator funcs = fm.getFunctions(true);

        println("======================================================================");
        println("FIRMWARE ANALYSIS - Wobkey Crush 80 (TLSR RISC-V)");
        println("======================================================================");

        // List all functions
        int count = 0;
        while (funcs.hasNext()) {
            Function fn = funcs.next();
            long entry = fn.getEntryPoint().getOffset();
            long size = fn.getBody().getNumAddresses();
            printf("  0x%05X  %-40s  size=%d\n", entry, fn.getName(), size);
            count++;
        }
        println("Total functions: " + count);

        // Decompile functions near our targets
        long[] targets = {
            0x0DC9CL,  // effect bounds check area
            0x0D5E0L,  // RGB handler area
            0x04488L,  // addi x15,x0,18
            0x03B0CL,  // addi x15,x0,19
            0x15220L,  // VIA dispatch candidate
        };

        println("\n--- Decompiling target regions ---");
        for (long addr : targets) {
            Function fn = fm.getFunctionContaining(toAddr(addr));
            if (fn == null) {
                printf("\n0x%05X: No function found\n", addr);
                continue;
            }
            long entry = fn.getEntryPoint().getOffset();
            printf("\n======================================================================\n");
            printf("Function at 0x%05X (contains 0x%05X) - %s, size=%d\n",
                   entry, addr, fn.getName(), fn.getBody().getNumAddresses());
            printf("======================================================================\n");

            DecompileResults result = decomp.decompileFunction(fn, 60, null);
            if (result != null && result.decompileCompleted()) {
                String code = result.getDecompiledFunction().getC();
                String[] lines = code.split("\n");
                int max = Math.min(lines.length, 300);
                for (int i = 0; i < max; i++) {
                    println("  " + lines[i]);
                }
                if (lines.length > max) {
                    printf("  ... (%d more lines)\n", lines.length - max);
                }
            } else {
                println("  (decompilation failed)");
            }
        }

        // Score all functions for VIA-like patterns
        println("\n--- Searching ALL functions for VIA handler patterns ---");
        funcs = fm.getFunctions(true);
        while (funcs.hasNext()) {
            Function fn = funcs.next();
            DecompileResults result = decomp.decompileFunction(fn, 30, null);
            if (result == null || !result.decompileCompleted()) continue;
            if (result.getDecompiledFunction() == null) continue;

            String code = result.getDecompiledFunction().getC();
            int score = 0;
            boolean has7 = code.contains("== 7") || code.contains("!= 7") || code.contains("== 0x7");
            boolean has8 = code.contains("== 8") || code.contains("!= 8") || code.contains("== 0x8");
            boolean has3 = code.contains("== 3") || code.contains("!= 3") || code.contains("== 0x3");
            boolean has18 = code.contains("0x12") || code.contains("== 18");
            boolean has9 = code.contains("== 9") || code.contains("< 10") || code.contains("< 0xa");
            if (has7) score++;
            if (has8) score++;
            if (has3) score++;
            if (has18) score++;
            if (has9) score++;

            if (score >= 3) {
                long entry = fn.getEntryPoint().getOffset();
                printf("\n======================================================================\n");
                printf("** MATCH (score %d/5) at 0x%05X - %s\n", score, entry, fn.getName());
                printf("   7=%b 8=%b 3=%b 18=%b 9=%b\n", has7, has8, has3, has18, has9);
                printf("======================================================================\n");
                String[] lines = code.split("\n");
                int max = Math.min(lines.length, 400);
                for (int i = 0; i < max; i++) {
                    println("  " + lines[i]);
                }
            }
        }

        decomp.dispose();
        println("\n--- Analysis complete ---");
    }
}
