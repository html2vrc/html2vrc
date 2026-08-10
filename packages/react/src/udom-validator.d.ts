declare module "@html2vrc/udom" {
  export interface UdomDiagnostic {
    code: string;
    severity: "error";
    pointer: string;
    nodeId?: string;
    message: string;
  }

  export interface UdomValidationOptions {
    supportedExtensions?: string[];
    supportedVersion?: string;
  }

  export interface UdomValidationResult {
    valid: boolean;
    diagnostics: UdomDiagnostic[];
  }

  export function validateUdom(
    document: unknown,
    options?: UdomValidationOptions
  ): UdomValidationResult;
}

