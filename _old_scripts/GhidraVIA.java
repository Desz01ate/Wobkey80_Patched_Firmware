// Decompile specific functions in the VIA handler area
// @category Analysis

import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.cmd.disassemble.DisassembleCommand;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.address.Address;
import ghidra.program.model.symbol.SourceType;

public class GhidraVIA extends GhidraScript {

    @Override
    public void run() throws Exception {
        DecompInterface decomp = new DecompInterface();
        decomp.openProgram(currentProgram);
        FunctionManager fm = currentProgram.getFunctionManager();

        // Decompile all functions near the VIA handler area
        long[] targets = {
            0x0D12CL,  // size=488 (existing)
            0x0D3F4L,  // size=82 (existing)
            0x0D488L,  // size=44 (existing)
            0x0DEDCL,  // size=86 (existing)
            0x0DF34L,  // size=174 (existing)
            0x0CEE0L,  // size=246 (existing)
            0x0C794L,  // size=102 (existing)
            0x0C490L,  // size=46 (existing)
            0x0C39CL,  // size=164 (existing)
            0x0C25CL,  // size=80 (existing)
            // Large functions that might be VIA dispatcher
            0x10A8CL,  // size=964 (switch)
            0x11B58L,  // size=1012
            0x1455CL,  // size=780
            0x16414L,  // size=1002
            0x16978L,  // size=820
        };

        // Also try to find the function containing 0x0DC9C by
        // disassembling backwards to find a proper function start
        println("=== Trying to find function containing 0x0DC9C ===");
        // Scan backwards from 0x0DC9C to find function prologue
        for (long scan = 0x0D500L; scan < 0x0DEDCL; scan += 2) {
            Address a = toAddr(scan);
            // Disassemble at this point
            DisassembleCommand cmd = new DisassembleCommand(a, null, true);
            cmd.applyTo(currentProgram);
        }
        // Now try to create one big function encompassing the area
        try {
            Address start = toAddr(0x0D554L);
            createFunction(start, "via_custom_handler_maybe");
            println("Created function at 0x0D554");
        } catch (Exception e) {
            println("Could not create at 0x0D554: " + e.getMessage());
        }
        // Try several potential entry points
        for (long entry = 0x0D4C0L; entry < 0x0D600L; entry += 4) {
            try {
                Address a = toAddr(entry);
                Function existing = fm.getFunctionContaining(a);
                if (existing != null) continue;
                createFunction(a, null);
            } catch (Exception e) {
                // ignore
            }
        }

        // Check what function now contains 0x0DC9C
        Function containsFn = fm.getFunctionContaining(toAddr(0x0DC9CL));
        if (containsFn != null) {
            printf("0x0DC9C is in function %s at 0x%X\n",
                   containsFn.getName(), containsFn.getEntryPoint().getOffset());
        } else {
            println("0x0DC9C still not in any function");
        }

        // Decompile all targets
        for (long addr : targets) {
            Function fn = fm.getFunctionAt(toAddr(addr));
            if (fn == null) {
                fn = fm.getFunctionContaining(toAddr(addr));
            }
            if (fn == null) {
                printf("\n0x%05X: No function\n", addr);
                continue;
            }

            long entry = fn.getEntryPoint().getOffset();
            long size = fn.getBody().getNumAddresses();

            DecompileResults result = decomp.decompileFunction(fn, 60, null);
            if (result == null || !result.decompileCompleted() || result.getDecompiledFunction() == null) {
                printf("\n0x%05X -> 0x%05X (%s, size=%d): decompile failed\n", addr, entry, fn.getName(), size);
                continue;
            }

            String code = result.getDecompiledFunction().getC();
            printf("\n======================================================================\n");
            printf("0x%05X -> Function 0x%05X (%s, size=%d)\n", addr, entry, fn.getName(), size);
            printf("======================================================================\n");
            println(code);
        }

        // Decompile any new functions we created in the gap
        println("\n=== Functions in gap 0x0D4B4 - 0x0DEDC ===");
        Function fn = fm.getFunctionAfter(toAddr(0x0D4B3L));
        while (fn != null && fn.getEntryPoint().getOffset() < 0x0DEDCL) {
            long entry = fn.getEntryPoint().getOffset();
            long size = fn.getBody().getNumAddresses();
            DecompileResults result = decomp.decompileFunction(fn, 60, null);
            if (result != null && result.decompileCompleted() && result.getDecompiledFunction() != null) {
                String code = result.getDecompiledFunction().getC();
                printf("\n--- Gap function 0x%05X (%s, size=%d) ---\n", entry, fn.getName(), size);
                println(code);
            } else {
                printf("\n--- Gap function 0x%05X (%s, size=%d) --- FAILED\n", entry, fn.getName(), size);
            }
            fn = fm.getFunctionAfter(fn.getEntryPoint());
        }

        decomp.dispose();
        println("\n--- VIA analysis complete ---");
    }
}
